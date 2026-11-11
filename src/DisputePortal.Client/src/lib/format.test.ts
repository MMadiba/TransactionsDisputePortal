import { describe, expect, it } from 'vitest'
import { formatDate, formatDateTime, formatMoney } from './format'

describe('formatting', () => {
  it('shows money like a statement: currency code, minus sign, two decimals', () => {
    expect(formatMoney(-1250, 'ZAR')).toBe('-ZAR 1,250.00')
    expect(formatMoney(15000.5, 'ZAR')).toBe('ZAR 15,000.50')
  })

  it("shows times in the bank's time zone, whatever the browser's", () => {
    // 22:30 UTC on 31 October is already 1 November in Johannesburg (UTC+2).
    expect(formatDate('2026-10-31T22:30:00Z')).toBe('01 Nov 2026')
    expect(formatDateTime('2026-10-31T22:30:00Z')).toBe('01 Nov 2026, 00:30')
  })
})
