<# Runs read-only cross-database checks against the isolated SWC-136 MySQL container. #>
[CmdletBinding()]
param(
    [string]$ComposeProject = "swiftcare-swc136",
    [string]$QueueDatabase = "queuedb_swc136",
    [string]$MedicalRecordDatabase = "medicalrecorddb_swc136",
    [string]$PrescriptionDatabase = "prescriptiondb_swc136",
    [string]$PatientDatabase = "patientdb_swc136",
    [string]$NotificationDatabase = "notificationdb_swc136",
    [Parameter(Mandatory)][string]$OutputPath
)

$ErrorActionPreference = "Stop"
if ($ComposeProject -ne "swiftcare-swc136") {
    throw "Refusing to inspect '$ComposeProject'. This script is restricted to swiftcare-swc136."
}
if ([string]::IsNullOrWhiteSpace($env:MYSQL_ROOT_PASSWORD)) {
    throw "MYSQL_ROOT_PASSWORD must be set for the isolated SWC-136 database."
}
foreach ($database in @($QueueDatabase,$MedicalRecordDatabase,$PrescriptionDatabase,$PatientDatabase,$NotificationDatabase)) {
    if ($database -notmatch '^[A-Za-z0-9_]+_swc136$') { throw "Unsafe database name: $database" }
}

$mysqlContainer = docker ps `
    --filter "label=com.docker.compose.project=$ComposeProject" `
    --filter "label=com.docker.compose.service=mysql" `
    --format "{{.Names}}" | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($mysqlContainer)) { throw "The isolated SWC-136 MySQL container is not running." }

$queries = @"
SELECT 'complete_consultation_without_completed_queue', COUNT(*)
FROM $MedicalRecordDatabase.Consultations c
LEFT JOIN $QueueDatabase.QueueEntries q ON q.Id=c.QueueId
WHERE c.Status='COMPLETE' AND (q.Status IS NULL OR q.Status<>'Completed')
UNION ALL
SELECT 'completed_queue_without_complete_consultation', COUNT(*)
FROM $QueueDatabase.QueueEntries q
LEFT JOIN $MedicalRecordDatabase.Consultations c ON c.QueueId=q.Id AND c.Status='COMPLETE'
WHERE q.Status='Completed' AND c.Id IS NULL
UNION ALL
SELECT 'completed_consultation_without_vitals', COUNT(*)
FROM $MedicalRecordDatabase.Consultations c
LEFT JOIN $MedicalRecordDatabase.VitalSigns v ON v.ConsultationId=c.Id
WHERE c.Status='COMPLETE' AND v.Id IS NULL
UNION ALL
SELECT 'completed_consultation_without_event_id', COUNT(*)
FROM $MedicalRecordDatabase.Consultations WHERE Status='COMPLETE' AND EventId IS NULL
UNION ALL
SELECT 'duplicate_consultation_event_id', COUNT(*) FROM (
  SELECT EventId FROM $MedicalRecordDatabase.Consultations WHERE EventId IS NOT NULL GROUP BY EventId HAVING COUNT(*)>1
) d
UNION ALL
SELECT 'multiple_active_entries_per_doctor', COUNT(*) FROM (
  SELECT DoctorId FROM $QueueDatabase.QueueEntries WHERE Status='InConsultation' AND DoctorId IS NOT NULL GROUP BY DoctorId HAVING COUNT(*)>1
) d
UNION ALL
SELECT 'multiple_active_entries_per_room', COUNT(*) FROM (
  SELECT RoomNumber FROM $QueueDatabase.QueueEntries WHERE Status='InConsultation' AND RoomNumber IS NOT NULL GROUP BY RoomNumber HAVING COUNT(*)>1
) d
UNION ALL
SELECT 'called_fields_inconsistent', COUNT(*) FROM $QueueDatabase.QueueEntries
WHERE (Status IN ('InConsultation','Completed')) <> (DoctorId IS NOT NULL AND DoctorName IS NOT NULL AND RoomNumber IS NOT NULL AND CalledAt IS NOT NULL)
UNION ALL
SELECT 'consultations_with_multiple_prescriptions', COUNT(*) FROM (
  SELECT ConsultationId FROM $PrescriptionDatabase.Prescriptions GROUP BY ConsultationId HAVING COUNT(*)>1
) d
UNION ALL
SELECT 'consultation_has_prescription_and_no_prescription', COUNT(*)
FROM $PrescriptionDatabase.Prescriptions p
JOIN $PrescriptionDatabase.NoPrescriptionDecisions n ON n.ConsultationId=p.ConsultationId
UNION ALL
SELECT 'prescription_without_complete_consultation', COUNT(*)
FROM $PrescriptionDatabase.Prescriptions p
LEFT JOIN $MedicalRecordDatabase.Consultations c ON c.Id=p.ConsultationId
WHERE c.Status IS NULL OR c.Status<>'COMPLETE'
UNION ALL
SELECT 'dispense_fields_inconsistent', COUNT(*) FROM $PrescriptionDatabase.Prescriptions
WHERE (Status='DISPENSED') <> (DispensedAt IS NOT NULL AND DispensedBy IS NOT NULL)
UNION ALL
SELECT 'outcome_without_matching_record', COUNT(*)
FROM $PrescriptionDatabase.ConsultationOutcomes o
LEFT JOIN $PrescriptionDatabase.Prescriptions p ON p.ConsultationId=o.ConsultationId AND o.Outcome='PRESCRIPTION'
LEFT JOIN $PrescriptionDatabase.NoPrescriptionDecisions n ON n.ConsultationId=o.ConsultationId AND o.Outcome='NOT_REQUIRED'
WHERE (o.Outcome='PRESCRIPTION' AND p.Id IS NULL) OR (o.Outcome='NOT_REQUIRED' AND n.Id IS NULL)
UNION ALL
SELECT 'duplicate_notification_event_id', COUNT(*) FROM (
  SELECT EventId FROM $NotificationDatabase.Notifications GROUP BY EventId HAVING COUNT(*)>1
) d
UNION ALL
SELECT 'notification_for_unknown_patient', COUNT(*)
FROM $NotificationDatabase.Notifications n
LEFT JOIN $PatientDatabase.Patients p ON p.Id=n.PatientId
WHERE p.Id IS NULL;
"@

$raw = docker exec -e "MYSQL_PWD=$($env:MYSQL_ROOT_PASSWORD)" $mysqlContainer `
    mysql -uroot --batch --skip-column-names -e $queries
if ($LASTEXITCODE -ne 0) { throw "MySQL integrity query failed." }

$results = foreach ($line in $raw) {
    $parts = $line -split "`t"
    if ($parts.Count -eq 2) { [pscustomobject]@{ Check=$parts[0]; Violations=[int64]$parts[1] } }
}
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
$parent = Split-Path -Parent $resolvedOutput
if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent | Out-Null }
$results | Export-Csv -NoTypeInformation -Path $resolvedOutput
$results | Format-Table -AutoSize

$failures = @($results | Where-Object Violations -ne 0)
if ($failures.Count -gt 0) {
    throw "$($failures.Count) SWC-136 data-integrity checks failed."
}
Write-Output "All $($results.Count) SWC-136 data-integrity checks passed. Evidence: $resolvedOutput"
