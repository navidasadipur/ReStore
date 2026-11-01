import { User } from '../models/user';

export function withRoles(user: User): User {
    try {
        const segment = user.token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
        const bytes = atob(segment.padEnd(Math.ceil(segment.length / 4) * 4, '='));
        const claims = JSON.parse(decodeURIComponent(bytes.split('').map(char =>
            '%' + char.charCodeAt(0).toString(16).padStart(2, '0')).join('')));
        const roles = claims.role ?? claims['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'];
        return { ...user, roles: typeof roles === 'string' ? [roles] : Array.isArray(roles) ? roles : [] };
    } catch {
        return { ...user, roles: [] };
    }
}

export function readStoredUser(): User | null {
    try {
        const user = JSON.parse(localStorage.getItem('user') ?? 'null');
        if (user && typeof user.email === 'string' && typeof user.token === 'string') return user;
    } catch { /* Invalid local state must not prevent application startup. */ }
    localStorage.removeItem('user');
    return null;
}
