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

vi.mock('@/lib/auth', () => ({
  useAuthSession: () => ({
    session: {
      accessToken: 'token',
      expiresAtUtc: '2026-05-09T14:00:00Z',
      tenantId: 'tenant-1',
      tenantSlug: 'demo',
      operatorAccountId: 'operator-1',
      email: 'rep@scout.local',
      displayName: 'Jordan Kim',
      role: 'sales_rep',
    },
    signIn: vi.fn(),
    signOut: vi.fn(),
  }),
}))

describe('AppShell', () => {
  it('hides admin-only navigation for sales reps', () => {
    render(<AppShell />)

    expect(screen.getByRole('link', { name: '1. Why Scout' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: '360 Customer Profile' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Sales Reference Intelligence' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Sales Reference Agent' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Operational Overview' })).toBeInTheDocument()
    expect(screen.queryByText('Data Sources')).not.toBeInTheDocument()
    expect(screen.queryByText('Selector Builder')).not.toBeInTheDocument()
    expect(screen.queryByText('Audit Log')).not.toBeInTheDocument()
  })
})
