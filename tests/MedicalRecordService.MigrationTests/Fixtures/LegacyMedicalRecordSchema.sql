CREATE TABLE IF NOT EXISTS ConsultationTemplates (
    Id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    Name VARCHAR(150) NOT NULL,
    Symptoms TEXT NOT NULL,
    ExaminationFindings TEXT NOT NULL,
    Notes TEXT NOT NULL,
    IsActive BOOLEAN NOT NULL DEFAULT TRUE,
    CreatedAt DATETIME(6) NOT NULL,
    PRIMARY KEY (Id),
    UNIQUE KEY UX_ConsultationTemplates_Name (Name)
);

CREATE TABLE IF NOT EXISTS Consultations (
    Id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    PatientId CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    QueueId CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    DoctorId CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    DoctorName VARCHAR(200) NOT NULL,
    RoomNumber VARCHAR(50) NOT NULL,
    Symptoms TEXT NOT NULL,
    ExaminationFindings TEXT NULL,
    Diagnosis TEXT NOT NULL,
    Notes TEXT NULL,
    TemplateId CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
    TemplateName VARCHAR(150) NULL,
    ConsultationDate DATETIME(6) NOT NULL,
    CreatedAt DATETIME(6) NOT NULL,
    PRIMARY KEY (Id),
    UNIQUE KEY UX_Consultations_QueueId (QueueId),
    KEY IX_Consultations_PatientId (PatientId),
    KEY IX_Consultations_DoctorId (DoctorId),
    KEY IX_Consultations_ConsultationDate (ConsultationDate),
    CONSTRAINT FK_Consultations_ConsultationTemplates_TemplateId
        FOREIGN KEY (TemplateId) REFERENCES ConsultationTemplates (Id)
        ON DELETE RESTRICT
);

INSERT IGNORE INTO ConsultationTemplates (
    Id,
    Name,
    Symptoms,
    ExaminationFindings,
    Notes,
    IsActive,
    CreatedAt
)
VALUES
    (
        '00000000-0000-0000-0000-000000000001',
        'General Consultation',
        'Presenting symptoms:\n- ',
        'Examination findings:\n- ',
        'Assessment and plan:\n- ',
        TRUE,
        UTC_TIMESTAMP(6)
    ),
    (
        '00000000-0000-0000-0000-000000000002',
        'Respiratory Consultation',
        'Respiratory symptoms:\n- ',
        'Respiratory examination findings:\n- ',
        'Respiratory assessment and plan:\n- ',
        TRUE,
        UTC_TIMESTAMP(6)
    ),
    (
        '00000000-0000-0000-0000-000000000003',
        'Gastrointestinal Consultation',
        'Gastrointestinal symptoms:\n- ',
        'Abdominal examination findings:\n- ',
        'Gastrointestinal assessment and plan:\n- ',
        TRUE,
        UTC_TIMESTAMP(6)
    ),
    (
        '00000000-0000-0000-0000-000000000004',
        'Musculoskeletal Consultation',
        'Musculoskeletal symptoms:\n- ',
        'Musculoskeletal examination findings:\n- ',
        'Musculoskeletal assessment and plan:\n- ',
        TRUE,
        UTC_TIMESTAMP(6)
    );
