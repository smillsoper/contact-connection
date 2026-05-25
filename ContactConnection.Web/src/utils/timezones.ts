export interface TimezoneOption {
  value: string
  label: string
}

export const TIMEZONES: TimezoneOption[] = [
  { value: 'America/New_York',    label: 'Eastern Time (EST / EDT)' },
  { value: 'America/Chicago',     label: 'Central Time (CST / CDT)' },
  { value: 'America/Denver',      label: 'Mountain Time (MST / MDT)' },
  { value: 'America/Phoenix',     label: 'Mountain Time – Arizona (MST, no DST)' },
  { value: 'America/Los_Angeles', label: 'Pacific Time (PST / PDT)' },
  { value: 'America/Anchorage',   label: 'Alaska Time (AKST / AKDT)' },
  { value: 'Pacific/Honolulu',    label: 'Hawaii Time (HST, no DST)' },
  { value: 'America/Toronto',     label: 'Eastern Time – Toronto (EST / EDT)' },
  { value: 'America/Vancouver',   label: 'Pacific Time – Vancouver (PST / PDT)' },
  { value: 'Europe/London',       label: 'London (GMT / BST)' },
  { value: 'Europe/Paris',        label: 'Paris / Berlin (CET / CEST)' },
  { value: 'UTC',                 label: 'UTC' },
]

export function timezoneLabel(value: string): string {
  return TIMEZONES.find((tz) => tz.value === value)?.label ?? value
}
