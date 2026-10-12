import type { VitalSigns } from '../api/consultations';

export type TrendMetric =
  | 'systolicBloodPressure'
  | 'temperatureCelsius'
  | 'pulseRate'
  | 'respiratoryRate'
  | 'oxygenSaturation'
  | 'weightKilograms'
  | 'bmi';

export type TrendDirection = 'up' | 'down' | 'unchanged';

export interface VitalTrend {
  direction: TrendDirection;
  previousValue: number;
}

// Readings are ordered newest first, so the baseline for a reading is the nearest older
// reading that actually recorded this measurement. A measurement skipped at one visit
// does not break the comparison for the visit after it.
export function trendFor(
  readings: readonly VitalSigns[],
  index: number,
  metric: TrendMetric,
): VitalTrend | null {
  const current = readings[index]?.[metric];
  if (current === null || current === undefined) {
    return null;
  }

  for (let olderIndex = index + 1; olderIndex < readings.length; olderIndex += 1) {
    const previous = readings[olderIndex][metric];
    if (previous === null || previous === undefined) {
      continue;
    }

    return {
      direction: current > previous ? 'up' : current < previous ? 'down' : 'unchanged',
      previousValue: previous,
    };
  }

  return null;
}
