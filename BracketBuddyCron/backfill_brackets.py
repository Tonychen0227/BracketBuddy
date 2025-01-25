import os
import traceback
from datetime import datetime
from time import sleep

from logger import Logger
from operations_manager import OperationsManager


if __name__ == '__main__':
    logger = Logger(f"{os.environ['SMASH_EXPLORER_LOG_ROOT']}/backfill")
    operations = OperationsManager(logger, "SMASHGG_KEYS_BACKFILL")

    mutex_name = "backfill"

    can_continue = operations.ensure_and_add_mutex(mutex_name)

    if not can_continue:
        logger.log("Quitting out Backfill Script because there is an ongoing backfill script")
        exit()

    logger.log("Starting Backfill, Mutex ensured")

    new_events = [1103262]

    current_tournaments = operations.get_active_current_tournaments()

    for current_tournament in current_tournaments:
        new_events.extend([int(x["Id"]) for x in current_tournament["Events"]])

    events_size = len(new_events)

    for x in range(0, 10):
        logger.log(f"Backfilling for the #{x + 1} time")
        events_count = 0
        for event_id in new_events:
            event_id = str(event_id)
            events_count += 1
            logger.log(f"Backfill operation creating new events - {events_count} of {events_size}")

            try:
                created_event = operations.get_and_create_event(event_id)
                operations.get_and_create_entrants_for_event(event_id)
                operations.update_event_sets(event_id, created_event)
            except:
                traceback.print_exc()
                logger.log(f"Issue backfilling {event_id}, skipping")

        if datetime.now().second >= 45:
            break
        else:
            sleep(10)

    logger.log("Removing mutex lock")
    operations.remove_mutex(mutex_name)
    logger.log("mutex lock successfully removed")
    logger.log("Backfill complete")
    exit()
