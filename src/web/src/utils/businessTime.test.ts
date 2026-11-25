import { describe, expect, it } from 'vitest';
import {
  formatDateOnlyUtc,
  getBusinessDate,
  getBusinessYear,
  parseDateOnlyUtc,
} from './businessTime';

describe('business time utilities', () => {
  it('uses Vietnam business date before and after midnight UTC+07', () => {
    expect(getBusinessDate(new Date('2026-11-24T16:59:59.999Z'))).toBe('2026-11-24');
    expect(getBusinessDate(new Date('2026-11-24T17:00:00.000Z'))).toBe('2026-11-25');
  });

  it('moves to the next business year at midnight in Vietnam', () => {
    const beforeMidnight = new Date('2026-12-31T16:59:59.999Z');
    const atMidnight = new Date('2026-12-31T17:00:00.000Z');

    expect(getBusinessDate(beforeMidnight)).toBe('2026-12-31');
    expect(getBusinessYear(beforeMidnight)).toBe(2026);
    expect(getBusinessDate(atMidnight)).toBe('2027-01-01');
    expect(getBusinessYear(atMidnight)).toBe(2027);
  });

  it('parses and formats date-only values without using the browser timezone', () => {
    const date = parseDateOnlyUtc('2027-01-01');

    expect(date.toISOString()).toBe('2027-01-01T00:00:00.000Z');
    expect(formatDateOnlyUtc(date)).toBe('2027-01-01');
  });

  it('rejects malformed and impossible calendar dates', () => {
    expect(Number.isNaN(parseDateOnlyUtc('2027-1-01').getTime())).toBe(true);
    expect(Number.isNaN(parseDateOnlyUtc('2027-02-29').getTime())).toBe(true);
    expect(Number.isNaN(parseDateOnlyUtc('2027-13-01').getTime())).toBe(true);
  });

  it('rejects invalid Date values when resolving or formatting a business date', () => {
    expect(() => getBusinessDate(new Date(Number.NaN))).toThrow(RangeError);
    expect(() => formatDateOnlyUtc(new Date(Number.NaN))).toThrow(RangeError);
  });
});
