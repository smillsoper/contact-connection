export interface TimezoneOption {
  value: string
  label: string
}

export interface TimezoneGroup {
  group: string
  options: TimezoneOption[]
}

export const TIMEZONE_GROUPS: TimezoneGroup[] = [
  {
    group: 'United States',
    options: [
      { label: 'Eastern Time (ET)',               value: 'America/New_York' },
      { label: 'Central Time (CT)',                value: 'America/Chicago' },
      { label: 'Mountain Time (MT)',               value: 'America/Denver' },
      { label: 'Pacific Time (PT)',                value: 'America/Los_Angeles' },
      { label: 'Alaska Time (AKT)',                value: 'America/Anchorage' },
      { label: 'Hawaii–Aleutian (HT)',             value: 'Pacific/Honolulu' },
      { label: 'Arizona — no DST',                 value: 'America/Phoenix' },
      { label: 'Indiana East — no DST',            value: 'America/Indiana/Indianapolis' },
    ],
  },
  {
    group: 'Canada',
    options: [
      { label: 'Atlantic Time (AT)',               value: 'America/Halifax' },
      { label: 'Newfoundland (NT)',                value: 'America/St_Johns' },
      { label: 'Eastern Time – Toronto (ET)',      value: 'America/Toronto' },
      { label: 'Pacific Time – Vancouver (PT)',    value: 'America/Vancouver' },
    ],
  },
  {
    group: 'Latin America',
    options: [
      { label: 'Mexico City (CT)',                 value: 'America/Mexico_City' },
      { label: 'São Paulo',                        value: 'America/Sao_Paulo' },
    ],
  },
  {
    group: 'Europe',
    options: [
      { label: 'London (GMT/BST)',                 value: 'Europe/London' },
      { label: 'Paris / Berlin (CET/CEST)',        value: 'Europe/Paris' },
      { label: 'Helsinki / Athens (EET/EEST)',     value: 'Europe/Helsinki' },
      { label: 'Moscow (MSK)',                     value: 'Europe/Moscow' },
    ],
  },
  {
    group: 'Asia / Pacific',
    options: [
      { label: 'India Standard Time (IST)',        value: 'Asia/Kolkata' },
      { label: 'China Standard Time (CST)',        value: 'Asia/Shanghai' },
      { label: 'Japan Standard Time (JST)',        value: 'Asia/Tokyo' },
      { label: 'Australia Eastern (AEST/AEDT)',    value: 'Australia/Sydney' },
      { label: 'New Zealand (NZST/NZDT)',          value: 'Pacific/Auckland' },
    ],
  },
  {
    group: 'Other',
    options: [
      { label: 'UTC',                              value: 'UTC' },
    ],
  },
]

// Flat list for simple dropdowns / lookup
export const TIMEZONES: TimezoneOption[] = TIMEZONE_GROUPS.flatMap((g) => g.options)

export function timezoneLabel(value: string): string {
  return TIMEZONES.find((tz) => tz.value === value)?.label ?? value
}
