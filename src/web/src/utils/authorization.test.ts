import { describe, expect, it } from 'vitest';
import { hasAnyPermission, hasApprovalAccess, hasPermission, readPermissionGrants } from './authorization';

function createToken(payload: Record<string, unknown>): string {
  const encoded = btoa(JSON.stringify(payload))
    .replace(/=/g, '')
    .replace(/\+/g, '-')
    .replace(/\//g, '_');
  return `header.${encoded}.signature`;
}

describe('authorization helpers', () => {
  it('đọc và hợp nhất permission/scope từ JWT version 1', () => {
    const token = createToken({
      authz_v: '1',
      perm: JSON.stringify([
        { p: 'leaves.read', s: ['Self', 'Team'] },
        { p: 'leaves.read', s: ['All'] },
        { p: 'leaves.decide', s: ['Team'] },
      ]),
    });

    expect(readPermissionGrants(token)).toEqual([
      { p: 'leaves.read', s: ['Self', 'Team', 'All'] },
      { p: 'leaves.decide', s: ['Team'] },
    ]);
  });

  it('từ chối claim có authorization version không hỗ trợ', () => {
    const token = createToken({
      authz_v: '2',
      perm: JSON.stringify([{ p: 'leaves.decide', s: ['All'] }]),
    });

    expect(readPermissionGrants(token)).toEqual([]);
  });

  it('fail closed khi token hoặc permission JSON sai định dạng', () => {
    expect(readPermissionGrants('not-a-jwt')).toEqual([]);
    expect(readPermissionGrants(createToken({ authz_v: '1', perm: '{invalid' }))).toEqual([]);
  });

  it('so khớp permission và scope không phân biệt chữ hoa/chữ thường', () => {
    const grants = [{ p: 'Leaves.Decide', s: ['Team'] }];

    expect(hasPermission(grants, 'leaves.decide', ['team'])).toBe(true);
    expect(hasPermission(grants, 'leaves.decide', ['All'])).toBe(false);
    expect(hasAnyPermission(grants, [
      { permission: 'employees.read', scopes: ['All'] },
      { permission: 'leaves.decide', scopes: ['Team', 'All'] },
    ])).toBe(true);
  });

  it('yêu cầu read scope tương ứng với decision scope', () => {
    expect(hasApprovalAccess([
      { p: 'leaves.decide', s: ['Team'] },
      { p: 'leaves.read', s: ['Team'] },
    ])).toBe(true);
    expect(hasApprovalAccess([
      { p: 'leaves.decide', s: ['Team'] },
      { p: 'leaves.read', s: ['All'] },
    ])).toBe(true);
    expect(hasApprovalAccess([
      { p: 'leaves.decide', s: ['All'] },
      { p: 'leaves.read', s: ['Team'] },
    ])).toBe(false);
    expect(hasApprovalAccess([
      { p: 'leaves.decide', s: ['All'] },
      { p: 'leaves.read', s: ['All'] },
    ])).toBe(true);
    expect(hasApprovalAccess([{ p: 'leaves.decide', s: ['Team'] }])).toBe(false);
  });

  it('không cấp quyền khi thiếu grant', () => {
    expect(hasPermission(undefined, 'leaves.read', ['Self'])).toBe(false);
    expect(hasAnyPermission([], [{ permission: 'leaves.decide', scopes: ['Team'] }])).toBe(false);
  });
});
