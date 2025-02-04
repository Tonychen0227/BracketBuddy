import datetime
import os

from cosmos import CosmosDB
from smashggapi import API


class OperationsManager:
    def __init__(self, logger, env_keys="SMASHGG_KEYS"):
        endpoint = os.environ["COSMOS_ENDPOINT"]
        key = os.environ["COSMOS_KEY"]
        cosmos = CosmosDB(endpoint, key, logger)
        api = API(os.environ[env_keys], logger)

        self.cosmos = cosmos
        self.api = api
        self.logger = logger

    def ensure_and_add_mutex(self, name_mutex):
        return self.cosmos.ensure_and_add_mutex(name_mutex)

    def remove_mutex(self, name_mutex):
        return self.cosmos.remove_mutex(name_mutex)

    def get_active_current_tournaments(self):
        return self.cosmos.get_active_current_tournaments()

    def update_event_sets(self, event_id, created_event):
        current_time = int((datetime.datetime.now(datetime.timezone.utc) - datetime.timedelta(minutes=3)).timestamp())

        event = created_event

        video_game_id = event["videoGameId"]
        video_game_name = event["videoGameName"]

        sets = self.api.get_event_sets(event_id, video_game_id, video_game_name, event["setsLastUpdated"])

        self.cosmos.update_event_sets_last_updated(event_id, current_time)

        total_sets = len(sets)
        self.logger.log(
            f"Updating {total_sets} sets {[x['id'] for x in sets]} for event {event_id} with timestamp {event['setsLastUpdated']}")

        try:
            num_added = self.cosmos.create_sets(event_id, sets)
            if num_added < total_sets:
                self.logger.log(f"WTF: Added fewer sets than expected for {event_id}")
                raise ValueError()
        except:
            self.logger.log(f"WTF: Something wrong happened with cosmos create sets on {event_id}, creating 1by1")
            for tournament_set in sets:
                self.cosmos.create_set(tournament_set)

        for cosmos_set in self.cosmos.get_event_sets(event_id):
            if cosmos_set["id"] not in [x["id"] for x in sets]:
                cosmos_set["isFakeSet"] = True
                self.cosmos.create_set(cosmos_set)

    def get_and_create_event(self, event_id):
        event = self.api.get_event(event_id)

        if event is None:
            self.logger.log(f"WTF: {event_id} no longer exists")
            return None

        return self.cosmos.create_event(event)

    def get_and_create_entrants_for_event(self, event_id):
        current_time = int((datetime.datetime.now(datetime.timezone.utc) - datetime.timedelta(minutes=10)).timestamp())

        event = self.cosmos.get_event(event_id)

        entrants_last_updated = event["entrantsLastUpdated"]
        should_do_full_update = False

        if entrants_last_updated is None or entrants_last_updated < current_time:
            should_do_full_update = True
            self.cosmos.update_event_entrants_last_updated(event_id, datetime.datetime.now(datetime.timezone.utc).timestamp())

        event_entrants = self.api.get_ult_event_entrants(event_id)
        db_entrants = self.cosmos.get_event_entrants(event_id)

        event_entrant_ids = set([entrant["id"] for entrant in event_entrants])

        db_entrants_dict = {}
        db_entrants_by_id = {}
        for db_entrant in db_entrants:
            db_entrants_dict[db_entrant["id"]] = db_entrant["_self"]
            db_entrants_by_id[db_entrant["id"]] = db_entrant

        final_event_entrants = []
        if not should_do_full_update:
            for entrant in event_entrants:
                if entrant["id"] not in db_entrants_by_id:
                    final_event_entrants.append(entrant)
                    continue

                db_entrant = db_entrants_by_id[entrant["id"]]

                if "standing" in db_entrant and "standingIsFinal" in db_entrant \
                        and entrant["standingIsFinal"] == db_entrant["standingIsFinal"] and entrant["standing"] == \
                        db_entrant["standing"]:
                    continue

                final_event_entrants.append(entrant)

            self.logger.log(f"Did not need full update! Curated entrant list has size {len(final_event_entrants)}")
        else:
            final_event_entrants.extend(event_entrants)
            self.logger.log(f"Entrants needed full update! Entrant list has size {len(final_event_entrants)}")

        entrants_deleted = 0

        total_event_entrants = len(event_entrant_ids)
        try:
            num_added = self.cosmos.create_entrants(event_id, final_event_entrants, db_entrants_dict)
            if num_added < total_event_entrants:
                self.logger.log(f"WTF: Added fewer entrants than expected for {event_id}")
                raise ValueError()
        except:
            self.logger.log(f"WTF: Something wrong happened with cosmos create entrants on {event_id}, creating 1by1")
            for entrant in final_event_entrants:
                self.cosmos.create_entrant(entrant)

        for entrant_id in db_entrants_dict.keys():
            if entrant_id not in event_entrant_ids:
                self.cosmos.delete_entrant(event_id, entrant_id)
                entrants_deleted += 1

        self.logger.log(
            f"Processed {len(event_entrant_ids)} entrants for event {event_id} and {entrants_deleted} removed")
