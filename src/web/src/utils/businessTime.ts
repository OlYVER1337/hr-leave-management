export const BUSINESS_TIME_ZONE = 'Asia/Ho_Chi_Minh';

const businessDateFormatter = new Intl.DateTimeFormat('en-CA', {
  timeZone: BUSINESS_TIME_ZONE,
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
});

/**
 * Lấy ngày lịch theo múi giờ nghiệp vụ Việt Nam, không phụ thuộc timezone của trình duyệt.
 */
export function getBusinessDate(value: Date = new Date()): string {
  if (Number.isNaN(value.getTime())) {
    throw new RangeError('Cannot resolve the business date from an invalid Date.');
  }

  const values = new Map(
    businessDateFormatter
      .formatToParts(value)
      .map((part) => [part.type, part.value] as const),
  );
  const year = values.get('year');
  const month = values.get('month');
  const day = values.get('day');

  if (!year || !month || !day) {
    throw new Error('Could not resolve the Vietnam business date.');
  }

  return `${year}-${month}-${day}`;
}

export function getBusinessYear(value: Date = new Date()): number {
  return Number(getBusinessDate(value).slice(0, 4));
}

/**
 * Parse giá trị date-only (YYYY-MM-DD) thành UTC date.
 * Dùng UTC getters/setters sau khi parse để phép tính không bị lệch theo timezone
 * hoặc thay đổi DST của máy đang mở trình duyệt.
 */
export function parseDateOnlyUtc(value: string): Date {
  const match = /^(\\d{4})-(\\d{2})-(\\d{2})$/.exec(value);
  if (!match) return new Date(Number.NaN);

  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  const date = new Date(Date.UTC(year, month - 1, day));

  if (
    date.getUTCFullYear() !== year
    || date.getUTCMonth() !== month - 1
    || date.getUTCDate() !== day
  ) {
    return new Date(Number.NaN);
  }

  return date;
}

export function formatDateOnlyUtc(value: Date): string {
  if (Number.isNaN(value.getTime())) {
    throw new RangeError('Cannot format an invalid date-only value.');
  }

  return [
    value.getUTCFullYear(),
    String(value.getUTCMonth() + 1).padStart(2, '0'),
    String(value.getUTCDate()).padStart(2, '0'),
  ].join('-');
}
