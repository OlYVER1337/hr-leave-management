import type { PermissionGrant } from '../types';

export interface PermissionRequirement {
  permission: string;
  scopes?: readonly string[];
}

const SUPPORTED_SCOPES = ['Self', 'Team', 'All'] as const;

function decodeJwtPayload(accessToken: string): Record<string, unknown> | null {
  const parts = accessToken.split('.');
  if (parts.length !== 3 || !parts[1]) return null;

  try {
    const base64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64.padEnd(Math.ceil(base64.length / 4) * 4, '=');
    const binary = atob(padded);
    const bytes = Uint8Array.from(binary, (character) => character.charCodeAt(0));
    const parsed: unknown = JSON.parse(new TextDecoder().decode(bytes));

    return parsed && typeof parsed === 'object' && !Array.isArray(parsed)
      ? parsed as Record<string, unknown>
      : null;
  } catch {
    return null;
  }
}

/**
 * Đọc claim để điều khiển hiển thị giao diện.
 * Hàm này không xác thực chữ ký JWT; backend vẫn quyết định quyền cuối cùng.
 */
export function readPermissionGrants(
  accessToken: string | null | undefined,
): PermissionGrant[] {
  if (!accessToken) return [];

  const payload = decodeJwtPayload(accessToken);
  if (!payload || String(payload.authz_v ?? '') !== '1') return [];

  try {
    const rawPermissions = payload.perm;
    const parsed: unknown = typeof rawPermissions === 'string'
      ? JSON.parse(rawPermissions)
      : rawPermissions;

    if (!Array.isArray(parsed)) return [];

    const grants = new Map<string, PermissionGrant>();
    for (const item of parsed) {
      if (!item || typeof item !== 'object') continue;

      const candidate = item as { p?: unknown; s?: unknown };
      if (typeof candidate.p !== 'string' || !candidate.p.trim()) continue;
      if (!Array.isArray(candidate.s)) continue;

      const permission = candidate.p.trim();
      const key = permission.toLowerCase();
      const scopes = candidate.s.filter(
        (scope): scope is string =>
          typeof scope === 'string' &&
          SUPPORTED_SCOPES.some((supported) => supported.toLowerCase() === scope.toLowerCase()),
      );
      const existing = grants.get(key);

      grants.set(key, {
        p: existing?.p ?? permission,
        s: [...new Set([...(existing?.s ?? []), ...scopes])],
      });
    }

    return [...grants.values()];
  } catch {
    return [];
  }
}

/** Yêu cầu một permission; khi truyền scopes, chỉ cần khớp một scope. */
export function hasPermission(
  grants: readonly PermissionGrant[] | null | undefined,
  permission: string,
  scopes?: readonly string[],
): boolean {
  if (!Array.isArray(grants) || !permission.trim()) return false;

  const requiredPermission = permission.trim().toLowerCase();
  const requiredScopes = scopes?.map((scope) => scope.toLowerCase());

  return grants.some((grant) => {
    // Dữ liệu quyền có thể cũ hoặc hỏng trong session; luôn từ chối grant sai cấu trúc.
    if (!grant || typeof grant.p !== 'string' || !Array.isArray(grant.s)) return false;
    if (grant.p.trim().toLowerCase() !== requiredPermission) return false;
    if (!requiredScopes?.length) return true;
    return grant.s.some((scope) =>
      typeof scope === 'string' && requiredScopes.includes(scope.toLowerCase()));
  });
}

/** Cho phép truy cập khi có ít nhất một trong các cặp permission/scope. */
export function hasAnyPermission(
  grants: readonly PermissionGrant[] | null | undefined,
  requirements: readonly PermissionRequirement[],
): boolean {
  return requirements.some((requirement) =>
    hasPermission(grants, requirement.permission, requirement.scopes),
  );
}

/**
 * Inbox cần vừa có quyền duyệt vừa có quyền đọc chi tiết đơn.
 * Scope All của quyền duyệt yêu cầu scope All tương ứng cho leaves.read.
 */
export function hasApprovalAccess(
  grants: readonly PermissionGrant[] | null | undefined,
): boolean {
  const canDecideAll = hasPermission(grants, 'leaves.decide', ['All']);
  if (canDecideAll) {
    return hasPermission(grants, 'leaves.read', ['All']);
  }

  return hasPermission(grants, 'leaves.decide', ['Team']) &&
    hasPermission(grants, 'leaves.read', ['Team', 'All']);
}
