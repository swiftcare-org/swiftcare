import { apiRequest } from './client';

export interface ConsultationTemplate {
  id: string;
  name: string;
  symptoms: string;
  examinationFindings: string;
  notes: string;
}

export interface CreateConsultationRequestBody {
  queueId: string;
  patientId: string;
  symptoms: string;
  examinationFindings: string | null;
  diagnosis: string;
  notes: string | null;
  templateId: string | null;
}

export interface Consultation {
  id: string;
  queueId: string;
  patientId: string;
  doctorId: string;
  doctorName: string;
  roomNumber: string;
  symptoms: string;
  examinationFindings: string | null;
  diagnosis: string;
  notes: string | null;
  templateId: string | null;
  templateName: string | null;
  consultationDate: string;
}

export interface RecordVitalSignsRequestBody {
  systolicBloodPressure: number | null;
  diastolicBloodPressure: number | null;
  temperatureCelsius: number | null;
  pulseRate: number | null;
  respiratoryRate: number | null;
  oxygenSaturation: number | null;
  heightCentimeters: number | null;
  weightKilograms: number | null;
}

export interface VitalSigns {
  id: string;
  consultationId: string;
  systolicBloodPressure: number | null;
  diastolicBloodPressure: number | null;
  temperatureCelsius: number | null;
  pulseRate: number | null;
  respiratoryRate: number | null;
  oxygenSaturation: number | null;
  heightCentimeters: number | null;
  weightKilograms: number | null;
  bmi: number | null;
  recordedAt: string;
}

export function getConsultationTemplates(): Promise<ConsultationTemplate[]> {
  return apiRequest<ConsultationTemplate[]>('/api/templates');
}

export function createConsultation(
  request: CreateConsultationRequestBody,
): Promise<Consultation> {
  return apiRequest<Consultation>('/api/consultations', {
    method: 'POST',
    body: request,
  });
}

export function recordVitalSigns(
  consultationId: string,
  request: RecordVitalSignsRequestBody,
): Promise<VitalSigns> {
  return apiRequest<VitalSigns>(`/api/consultations/${consultationId}/vitals`, {
    method: 'POST',
    body: request,
  });
}
