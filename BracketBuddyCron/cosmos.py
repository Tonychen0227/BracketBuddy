import datetime
import os

from azure.cosmos import CosmosClient, exceptions
from azure.cosmos.exceptions import CosmosResourceNotFoundError


class CosmosDB:
    def __init__(self, endpoint, key, logger):
        self.database = CosmosClient(endpoint, key).get_database_client(os.environ["COSMOS_DB_NAME"])
        self.entrants = self.database.get_container_client("Entrants")
        self.events = self.database.get_container_client("Events")
        self.current_tournaments = self.database.get_container_client("CurrentTournaments")
        self.sets = self.database.get_container_client("Sets")
        self.mutex = self.database.get_container_client("Mutex")
        self.logger = logger

    def remove_mutex(self, name_mutex):
        try:
            self.mutex.delete_item(item=name_mutex, partition_key=name_mutex)
        except CosmosResourceNotFoundError:
            self.logger.log("Cannot delete mutex as it is already gone")
            pass

        return

    def ensure_and_add_mutex(self, name_mutex):
        try:
            mutex = self.mutex.read_item(item=name_mutex, partition_key=name_mutex)

            date_now = datetime.datetime.now(datetime.timezone.utc)
            cutoff_time = int((date_now - datetime.timedelta(minutes=10)).timestamp())
            mutex_time = int(mutex["_ts"])

            if mutex_time < cutoff_time:
                self.logger.log("Removing mutex as it became stale!")
                self.remove_mutex(name_mutex)
                self.mutex.upsert_item(body={"id": name_mutex})
                return True
        except CosmosResourceNotFoundError:
            self.mutex.upsert_item(body={"id": name_mutex})
            return True

        return False

    # region Entrants
    def __upsert_entrant(self, entrant):
        return self.entrants.upsert_item(body=entrant)

    def create_entrants(self, event_id, entrants, db_entrants_dict):
        for entrant in entrants:
            if entrant["id"] in db_entrants_dict:
                entrant["_self"] = db_entrants_dict[entrant["id"]]
        return self.entrants.scripts.execute_stored_procedure("bulkImport2", partition_key=event_id, params=[entrants])

    def create_entrant(self, entrant):
        self.__upsert_entrant(entrant)

    def get_event_entrants(self, event_id):
        response = self.entrants.query_items(query=f"SELECT k.id, k._self FROM k WHERE k.eventId = \"{event_id}\"",
                                             partition_key=event_id)
        return response

    def delete_entrant(self, event_id, entrant_id):
        self.entrants.delete_item(item=entrant_id, partition_key=event_id)

    # endregion Entrants

    # region Events
    def __upsert_event(self, event):
        return self.events.upsert_item(body=event)

    def create_event(self, event):
        existing_event = self.get_event(event["id"])
        event["setsLastUpdated"] = 1 if existing_event is None else existing_event["setsLastUpdated"]
        return self.__upsert_event(event)

    def get_event(self, event_id):
        try:
            response = self.events.read_item(item=str(event_id), partition_key=str(event_id))
        except exceptions.CosmosResourceNotFoundError:
            response = None

        return response

    def update_event_sets_last_updated(self, event_id, last_updated):
        event = self.get_event(event_id)
        event["setsLastUpdated"] = last_updated

        self.__upsert_event(event)

    # endregion Events

    # region Sets
    def __upsert_set(self, tournament_set):
        return self.sets.upsert_item(body=tournament_set)

    def get_event_sets(self, event_id):
        return self.sets.query_items(query=f"SELECT * FROM k WHERE k.eventId = \"{event_id}\"",
                                     partition_key=event_id)

    def create_sets(self, event_id, sets):
        return self.sets.scripts.execute_stored_procedure("bulkImport", partition_key=event_id, params=[sets])

    def create_set(self, tournament_set):
        self.__upsert_set(tournament_set)
    # endregion Sets

    # region Tournaments
    def get_active_current_tournaments(self):
        response = self.current_tournaments.query_items(
            query=f"SELECT * FROM k WHERE (k.isActive or k.IsActive)",
            enable_cross_partition_query=True)
        return response
    # endregion Tournaments
