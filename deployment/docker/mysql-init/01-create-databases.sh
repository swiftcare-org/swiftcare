#!/bin/bash
# Runs once, when the MySQL entrypoint initialises an empty data volume. Re-run with
# `docker compose down -v`, which destroys all local database data.
#
# Names and grantee come from the same .env values the services use in their connection
# strings, so the two cannot drift.

set -e

databases=(
  "${AUTH_DB_NAME:-swiftcare_auth}"
  "${PATIENT_DB_NAME:-swiftcare_patient}"
  "${QUEUE_DB_NAME:-swiftcare_queue}"
  "${MEDICAL_RECORD_DB_NAME:-swiftcare_medical_record}"
  "${PRESCRIPTION_DB_NAME:-swiftcare_prescription}"
  "${NOTIFICATION_DB_NAME:-swiftcare_notification}"
)

for database in "${databases[@]}"; do
  mysql --protocol=socket -u root -p"${MYSQL_ROOT_PASSWORD}" -e "
    CREATE DATABASE IF NOT EXISTS \`${database}\` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
    GRANT ALL PRIVILEGES ON \`${database}\`.* TO '${MYSQL_USER}'@'%';"
done

mysql --protocol=socket -u root -p"${MYSQL_ROOT_PASSWORD}" -e "FLUSH PRIVILEGES;"

echo "Created ${#databases[@]} databases and granted them to ${MYSQL_USER}"
