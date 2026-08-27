import type { ReactNode } from 'react'
import { render, screen } from '@testing-library/react'
import { AppShell } from '@/components/shell/app-shell'

const navigateMock = vi.fn()

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to, ...props }: { children: ReactNode; to: string }) => (
    <a href={to} {...props}>
      {children}
    </a>
  ),
  Outlet: () => <div>Outlet content</div>,
  useLocation: () => ({ pathname: '/overview' }),
  useNavigate: () => navigateMock,
}))

vi.mock('@/lib/env', () => ({
  env: {
    demoFallbackEnabled: false,
  },
}))

vi.mock('@/lib/auth', () => ({
  useAuthSession: () => ({
    session: {
      accessToken: 'token',
      expiresAtUtc: '2026-05-09T14:00:00Z',
      tenantId: 'tenant-1',
      tenantSlug: 'production-tenant',
      operatorAccountId: 'operator-1',
      email: 'rep@example.test',
      displayName: 'Jordan Kim',
      role: 'sales_rep',
    },
    signIn: vi.fn(),
    signOut: vi.fn(),
  }),
}))

describe('AppShell production navigation', () => {
  it('does not advertise sales reference consumers when demo fallback is disabled', () => {
    render(<AppShell />)

    expect(screen.getByRole('link', { name: '360 Customer Profile' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Operational Overview' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Sales Reference Intelligence' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Sales Reference Agent' })).not.toBeInTheDocument()
  })
})
