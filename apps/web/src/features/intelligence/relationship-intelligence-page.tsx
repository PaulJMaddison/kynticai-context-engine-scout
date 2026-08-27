import { useMemo, useState } from 'react'
import { Link } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import {
  ArrowRight,
  Braces,
  CalendarClock,
  CheckCircle2,
  Clock3,
  GitBranch,
  Mail,
  Network,
  Search,
  ShieldCheck,
  Sparkles,
  TriangleAlert,
} from 'lucide-react'
import { JsonViewer } from '@/components/data-display/json-viewer'
import { RelationshipJsonExplorer } from '@/features/intelligence/relationship-json-explorer'
import {
  Badge,
  Button,
  Card,
  Field,
  Input,
  MetricCard,
  PageHeader,
  Panel,
  Select,
} from '@/components/ui/primitives'
import { api } from '@/lib/api'
import { useAuthSession } from '@/lib/auth'
import { cn, formatConfidence, formatDateTime, humanizeEnum, safeJsonParse } from '@/lib/utils'
import type {
  ExactLinkedRecordSummaryResult,
  NextActionInput,
  SimilarPatternMatchResult,
  WeightedSignalResult,
} from '@/lib/types'

const lookupDefaults = {
  email: 'avery.stone@larkspur-logistics.example',
  contact: 'CON-10000',
  account: 'ACC-2000',
} satisfies Record<NextActionInput['subjectType'], string>

const subjectOptions = [
  { value: 'email', label: 'Email address' },
  { value: 'contact', label: 'Contact id' },
  { value: 'account', label: 'Account id' },
] satisfies Array<{ value: NextActionInput['subjectType']; label: string }>

const objectiveOptions = [
  { value: 'sale', label: 'Sale' },
  { value: 'conversion', label: 'Conversion' },
  { value: 'retention', label: 'Retention' },
  { value: 'support', label: 'Support' },
  { value: 'churn', label: 'Churn' },
] satisfies Array<{ value: NextActionInput['objective']; label: string }>

const purposeOptions = [
  { value: 'customer_outreach', label: 'Customer outreach' },
  { value: 'renewal_review', label: 'Renewal review' },
  { value: 'support_followup', label: 'Support follow-up' },
  { value: 'retention_checkin', label: 'Retention check-in' },
]

const actorRoleOptions = [
  { value: 'sales_rep', label: 'Sales rep view' },
  { value: 'tenant_admin', label: 'Tenant admin view' },
  { value: 'analyst', label: 'Analyst view' },
  { value: 'read_only', label: 'Read-only view' },
] satisfies Array<{ value: NextActionInput['actorRole']; label: string }>

function labelize(value: string) {
  return humanizeEnum(value.replace(/([a-z0-9])([A-Z])/g, '$1_$2'))
}

function recordTone(record: ExactLinkedRecordSummaryResult) {
  if (record.isMasked) {
    return 'warning' as const
  }

  if (record.recordType.includes('Outcome') || record.recordType.includes('Usage')) {
    return 'success' as const
  }

  if (record.recordType.includes('Support')) {
    return 'warning' as const
  }

  return 'accent' as const
}

function relationshipTone(linkKind: string) {
  return linkKind === 'probabilistic' ? ('accent' as const) : ('success' as const)
}

function formatContribution(value: number) {
  const points = Math.round(value * 100)
  return `${points > 0 ? '+' : ''}${points} pts`
}

function signalTone(signal: WeightedSignalResult) {
  if (signal.contribution < 0) {
    return 'danger' as const
  }

  if (signal.score >= 0.8) {
    return 'success' as const
  }

  return 'accent' as const
}

function patternTone(pattern: SimilarPatternMatchResult) {
  return pattern.outcome.toLowerCase() === 'won' ? ('success' as const) : ('warning' as const)
}

function CitationChips({
  citationIds,
  tone = 'accent',
}: {
  citationIds: string[]
  tone?: 'neutral' | 'success' | 'warning' | 'danger' | 'accent'
}) {
  return (
    <div className="flex flex-wrap gap-2">
      {citationIds.map((citationId) => (
        <Badge key={citationId} tone={tone} className="max-w-full">
          {citationId}
        </Badge>
      ))}
    </div>
  )
}

function PatternCard({
  pattern,
}: {
  pattern: SimilarPatternMatchResult
}) {
  return (
    <Card className="bg-ivory-25">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="text-xs uppercase tracking-[0.18em] text-sage-700">{pattern.matchId}</p>
          <h3 className="mt-2 break-words font-display text-2xl text-ink-950">
            {labelize(pattern.outcome)} pattern
          </h3>
          <p className="mt-1 break-words text-sm text-ink-600">
            {pattern.matchedAccountId} · {pattern.matchedSubjectType}
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Badge tone={patternTone(pattern)}>{formatConfidence(pattern.similarityScore)}</Badge>
          <Badge tone={pattern.outcomeWeight >= 0 ? 'success' : 'danger'}>
            {formatContribution(pattern.outcomeWeight)}
          </Badge>
        </div>
      </div>

      <div className="mt-4 flex flex-wrap gap-2">
        {pattern.relationshipTypes.slice(0, 5).map((type) => (
          <Badge key={type} tone="neutral">
            {labelize(type)}
          </Badge>
        ))}
      </div>

      <div className="mt-4 grid gap-2">
        {pattern.reasons.map((reason) => (
          <p key={reason} className="text-sm leading-7 text-ink-700">
            {reason}
          </p>
        ))}
      </div>

      <div className="mt-4">
        <CitationChips citationIds={pattern.citationIds} />
      </div>
    </Card>
  )
}

export function RelationshipIntelligencePage() {
  const { session } = useAuthSession()
  const tenantSlug = session?.tenantSlug ?? 'demo'
  const [subjectType, setSubjectType] = useState<NextActionInput['subjectType']>('email')
  const [subjectIdentifier, setSubjectIdentifier] = useState(lookupDefaults.email)
  const [objective, setObjective] = useState<NextActionInput['objective']>('sale')
  const [purpose, setPurpose] = useState('customer_outreach')
  const [actorRole, setActorRole] = useState<NextActionInput['actorRole']>('sales_rep')

  const request = useMemo<NextActionInput>(
    () => ({
      tenantSlug,
      subjectType,
      subjectIdentifier: subjectIdentifier.trim(),
      objective,
      purpose,
      actorRole,
    }),
    [actorRole, objective, purpose, subjectIdentifier, subjectType, tenantSlug],
  )

  const intelligenceQuery = useQuery({
    queryKey: ['relationship-intelligence', request],
    queryFn: () => api.generateNextAction(request),
    enabled: Boolean(session && request.subjectIdentifier && request.purpose),
    placeholderData: (previousData) => previousData,
  })

  const result = intelligenceQuery.data
  const evidenceTimeline = useMemo(
    () =>
      [...(result?.exactLinkedRecords.records ?? [])]
        .filter((record) => Boolean(record.observedAtUtc))
        .sort((left, right) => String(right.observedAtUtc).localeCompare(String(left.observedAtUtc))),
    [result?.exactLinkedRecords.records],
  )
  const wonPatterns = useMemo(
    () => result?.similarWonLostPatterns.filter((pattern) => pattern.outcome.toLowerCase() === 'won') ?? [],
    [result?.similarWonLostPatterns],
  )
  const lostPatterns = useMemo(
    () => result?.similarWonLostPatterns.filter((pattern) => pattern.outcome.toLowerCase() === 'lost') ?? [],
    [result?.similarWonLostPatterns],
  )
  const positiveSignals = useMemo(
    () => result?.weightedSignals.filter((signal) => signal.contribution >= 0) ?? [],
    [result?.weightedSignals],
  )
  const negativeSignals = useMemo(
    () => result?.weightedSignals.filter((signal) => signal.contribution < 0) ?? [],
    [result?.weightedSignals],
  )
  const cloudPayload = useMemo(
    () => safeJsonParse(result?.evidencePack.cloudAggregateUsagePayloadJson ?? '{}', {}),
    [result?.evidencePack.cloudAggregateUsagePayloadJson],
  )
  const totalRecords = result
    ? Object.values(result.exactLinkedRecords.recordCounts).reduce((total, count) => total + count, 0)
    : 0

  if (!session) {
    return null
  }

  return (
    <div className="grid gap-8">
      <PageHeader
        eyebrow="Synthetic sales reference"
        title="Reference relationship intelligence built outside Scout core."
        description="Resolve a synthetic email, contact, or account into exact linked records, Scout fallback weighted signals, similar won/lost patterns, a cited draft response, governance controls, and handoff JSON."
        actions={
          <>
            <Link to="/agent-playground">
              <Button variant="ghost">
                Example consumer
                <ArrowRight className="size-4" />
              </Button>
            </Link>
            <Button
              type="button"
              onClick={() => void intelligenceQuery.refetch()}
              disabled={intelligenceQuery.isFetching}
            >
              <Search className="size-4" />
              Refresh analysis
            </Button>
          </>
        }
      />

      <section className="grid gap-4 md:grid-cols-2 2xl:grid-cols-4">
        <MetricCard
          label="Recommendation confidence"
          value={formatConfidence(result?.confidence)}
          footnote="Composite confidence across exact evidence, Scout fallback signals, and similar-pattern support."
          accent="copper"
        />
        <MetricCard
          label="Exact linked records"
          value={String(totalRecords || '—')}
          footnote="Synthetic CRM, email, web, support, usage, billing, opportunity, and outcome records in the local evidence pack."
          accent="sage"
        />
        <MetricCard
          label="Relationships resolved"
          value={String(result?.relationships.length ?? '—')}
          footnote="Deterministic joins and local pattern links with explicit fallback weights and citations."
          accent="gold"
        />
        <MetricCard
          label="Masked fields"
          value={String(result?.governance.maskedFields.length ?? '—')}
          footnote="Governance indicators show when direct identifiers or sensitive fields are masked for the current view."
          accent="copper"
        />
      </section>

      <section className="grid gap-4 2xl:grid-cols-[340px_minmax(0,1fr)_420px]">
        <Panel eyebrow="Lookup" title="Resolve email, contact, or account">
          <div className="grid gap-5">
            <Field label="Lookup type">
              <Select
                value={subjectType}
                onChange={(event) => {
                  const nextType = event.target.value as NextActionInput['subjectType']
                  setSubjectType(nextType)
                  setSubjectIdentifier(lookupDefaults[nextType])
                }}
              >
                {subjectOptions.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </Select>
            </Field>

            <Field label="Identifier">
              <Input
                value={subjectIdentifier}
                onChange={(event) => setSubjectIdentifier(event.target.value)}
                placeholder={lookupDefaults[subjectType]}
              />
            </Field>

            <div className="grid gap-5 md:grid-cols-2 2xl:grid-cols-1">
              <Field label="Objective">
                <Select
                  value={objective}
                  onChange={(event) => setObjective(event.target.value as NextActionInput['objective'])}
                >
                  {objectiveOptions.map((option) => (
                    <option key={option.value} value={option.value}>
                      {option.label}
                    </option>
                  ))}
                </Select>
              </Field>

              <Field label="Purpose">
                <Select value={purpose} onChange={(event) => setPurpose(event.target.value)}>
                  {purposeOptions.map((option) => (
                    <option key={option.value} value={option.value}>
                      {option.label}
                    </option>
                  ))}
                </Select>
              </Field>
            </div>

            <Field label="Governance view">
              <Select
                value={actorRole}
                onChange={(event) => setActorRole(event.target.value as NextActionInput['actorRole'])}
              >
                {actorRoleOptions.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </Select>
            </Field>

            <Card className="bg-ink-950 text-ivory-50">
              <div className="flex items-start gap-3">
                <Network className="mt-1 size-5 text-copper-300" />
                <div>
                  <p className="font-display text-2xl">Synthetic demo source</p>
                  <p className="mt-2 text-sm leading-7 text-ivory-200">
                    {result
                      ? `${labelize(result.subjectType)} lookup resolved as ${result.subjectIdentifier}.`
                      : 'Resolving the relationship intelligence package.'}
                  </p>
                  <div className="mt-4 flex flex-wrap gap-2">
                    <Badge tone="accent">No real customer data</Badge>
                    <Badge tone="neutral">Customer-owned data plane</Badge>
                  </div>
                </div>
              </div>
            </Card>
          </div>
        </Panel>

        <div className="grid min-w-0 gap-4">
          <Panel
            eyebrow="Recommended next action"
            title={result?.recommendedNextAction.action ?? 'Resolving action'}
            action={
              result ? (
                <div className="flex flex-wrap gap-2">
                  <Badge tone="success">{formatConfidence(result.recommendedNextAction.score)}</Badge>
                  <Badge tone={result.draftResponse?.requiresHumanReview ? 'warning' : 'accent'}>
                    {result.draftResponse?.requiresHumanReview ? 'Review before send' : 'Ready to route'}
                  </Badge>
                </div>
              ) : null
            }
          >
            {result ? (
              <div className="grid gap-4">
                <Card className="bg-[linear-gradient(180deg,rgba(255,253,248,0.98),rgba(248,241,231,0.98))]">
                  <div className="flex items-start gap-3">
                    <Sparkles className="mt-1 size-5 shrink-0 text-copper-700" />
                    <div className="min-w-0">
                      <p className="text-sm font-semibold text-ink-950">{result.recommendedNextAction.timing}</p>
                      <p className="mt-3 text-sm leading-7 text-ink-700">
                        {result.recommendedNextAction.rationale}
                      </p>
                      <div className="mt-4">
                        <CitationChips citationIds={result.recommendedNextAction.citationIds} />
                      </div>
                    </div>
                  </div>
                </Card>

                <div className="grid gap-4 xl:grid-cols-[0.96fr_1.04fr]">
                  <Card className="bg-ivory-25">
                    <div className="flex items-center gap-3">
                      <GitBranch className="size-5 text-sage-700" />
                      <p className="font-display text-2xl text-ink-950">Weighted signals</p>
                    </div>
                    <div className="mt-4 grid gap-3">
                      {[...positiveSignals, ...negativeSignals].map((signal) => (
                        <div key={signal.signalKey} className="rounded-[20px] border border-ink-900/8 bg-ivory-50 px-4 py-4">
                          <div className="flex flex-wrap items-center justify-between gap-3">
                            <p className="font-semibold text-ink-950">{signal.label}</p>
                            <div className="flex flex-wrap gap-2">
                              <Badge tone={signalTone(signal)}>{formatContribution(signal.contribution)}</Badge>
                              <Badge tone="neutral">{formatConfidence(signal.score)}</Badge>
                            </div>
                          </div>
                          <div className="mt-3 h-2 overflow-hidden rounded-full bg-ink-950/8">
                            <div
                              className={cn(
                                'h-full rounded-full',
                                signal.contribution < 0 ? 'bg-rosewood-500' : 'bg-copper-500',
                              )}
                              style={{ width: `${Math.max(8, Math.round(signal.score * 100))}%` }}
                            />
                          </div>
                          <p className="mt-3 text-sm leading-7 text-ink-700">{signal.explanation}</p>
                          <div className="mt-3">
                            <CitationChips citationIds={signal.citationIds} tone="neutral" />
                          </div>
                        </div>
                      ))}
                    </div>
                  </Card>

                  <Card className="bg-ivory-25">
                    <div className="flex items-center gap-3">
                      <Mail className="size-5 text-copper-700" />
                      <p className="font-display text-2xl text-ink-950">Draft response with citations</p>
                    </div>
                    {result.draftResponse ? (
                      <div className="mt-4 grid gap-4">
                        <div>
                          <p className="text-xs uppercase tracking-[0.18em] text-sage-700">Subject</p>
                          <p className="mt-2 font-semibold text-ink-950">{result.draftResponse.subject}</p>
                        </div>
                        <pre className="overflow-auto whitespace-pre-wrap rounded-[20px] border border-ink-900/8 bg-ivory-50 px-4 py-4 text-sm leading-7 text-ink-800">
                          {result.draftResponse.body}
                        </pre>
                        <CitationChips citationIds={result.draftResponse.citationIds} />
                      </div>
                    ) : (
                      <p className="mt-4 text-sm leading-7 text-ink-700">
                        No draft response is required for this objective.
                      </p>
                    )}
                  </Card>
                </div>
              </div>
            ) : (
              <Card className="bg-ivory-25">Loading relationship intelligence...</Card>
            )}
          </Panel>

          <Panel eyebrow="Evidence timeline" title="Exact linked records behind the recommendation">
            <div className="relative grid gap-4 pl-8">
              <div className="absolute bottom-2 left-3 top-3 w-px bg-copper-200" />
              {evidenceTimeline.map((record) => (
                <div key={`${record.citationId}-${record.recordId}`} className="relative">
                  <div className="absolute -left-[31px] top-7 size-4 rounded-full border-4 border-ivory-100 bg-copper-500 shadow-sm" />
                  <Card className="bg-ivory-25">
                    <div className="flex flex-wrap items-start justify-between gap-3">
                      <div className="min-w-0">
                        <div className="flex flex-wrap gap-2">
                          <Badge tone={recordTone(record)}>{record.citationId}</Badge>
                          <Badge tone="neutral">{labelize(record.recordType)}</Badge>
                          {record.isMasked ? <Badge tone="warning">Masked</Badge> : null}
                        </div>
                        <h3 className="mt-3 break-words font-display text-2xl text-ink-950">{record.label}</h3>
                        <p className="mt-2 text-sm leading-7 text-ink-700">{record.summary}</p>
                      </div>
                      <p className="shrink-0 text-xs uppercase tracking-[0.18em] text-ink-500">
                        {formatDateTime(record.observedAtUtc)}
                      </p>
                    </div>
                    <div className="mt-4 grid gap-2 md:grid-cols-2">
                      {Object.entries(record.fields).slice(0, 6).map(([field, value]) => (
                        <div key={`${record.citationId}-${field}`} className="rounded-[16px] bg-ivory-50 px-3 py-3">
                          <p className="text-[11px] uppercase tracking-[0.16em] text-sage-700">{labelize(field)}</p>
                          <p className="mt-1 break-words text-sm text-ink-800">{value}</p>
                        </div>
                      ))}
                    </div>
                  </Card>
                </div>
              ))}
            </div>
          </Panel>
        </div>

        <div className="grid min-w-0 gap-4">
          <Panel eyebrow="Similar patterns" title="Won and lost journeys shaping the score">
            <div className="grid gap-4">
              {wonPatterns.map((pattern) => (
                <PatternCard key={pattern.matchId} pattern={pattern} />
              ))}
              {lostPatterns.map((pattern) => (
                <PatternCard key={pattern.matchId} pattern={pattern} />
              ))}
              {result && result.similarWonLostPatterns.length === 0 ? (
                <Card className="bg-ivory-25">
                  <p className="text-sm leading-7 text-ink-700">
                    No similar won or lost journeys were strong enough to include.
                  </p>
                </Card>
              ) : null}
            </div>
          </Panel>

          <Panel
            eyebrow="Governance and confidence"
            title="Masking, caveats, and customer data-plane boundaries"
            action={
              result ? (
                <Badge tone={result.governance.cloudPayloadContainsRawCustomerData ? 'danger' : 'success'}>
                  {result.governance.cloudPayloadContainsRawCustomerData ? 'Raw cloud payload' : 'Cloud aggregate usage'}
                </Badge>
              ) : null
            }
          >
            {result ? (
              <div className="grid gap-4">
                <Card className="bg-ivory-25">
                  <div className="flex items-start gap-3">
                    <ShieldCheck className="mt-1 size-5 text-sage-700" />
                    <div>
                      <p className="font-semibold text-ink-950">Customer data-plane posture</p>
                      <p className="mt-2 text-sm leading-7 text-ink-700">
                        Raw records stay in the {result.governance.dataPlane}; the cloud/control-plane payload is an aggregate projection.
                      </p>
                      <div className="mt-4 flex flex-wrap gap-2">
                        <Badge tone={result.governance.rawDataRetainedInCustomerDataPlane ? 'success' : 'warning'}>
                          Raw data retained locally
                        </Badge>
                        <Badge tone={result.governance.isAllowed ? 'success' : 'danger'}>
                          {result.governance.isAllowed ? 'Allowed' : 'Denied'}
                        </Badge>
                      </div>
                    </div>
                  </div>
                </Card>

                <div className="grid gap-3">
                  {result.caveats.map((caveat) => (
                    <div key={caveat} className="flex items-start gap-3 rounded-[20px] border border-gold-500/22 bg-gold-500/10 px-4 py-4">
                      <TriangleAlert className="mt-1 size-4 shrink-0 text-gold-700" />
                      <p className="text-sm leading-7 text-ink-800">{caveat}</p>
                    </div>
                  ))}
                </div>

                <Card className="bg-ivory-25">
                  <div className="flex items-center gap-3">
                    <CheckCircle2 className="size-5 text-sage-700" />
                    <p className="font-display text-2xl text-ink-950">Applied rules</p>
                  </div>
                  <div className="mt-4 flex flex-wrap gap-2">
                    {result.governance.appliedRules.map((rule) => (
                      <Badge key={rule} tone="neutral">
                        {rule}
                      </Badge>
                    ))}
                  </div>
                </Card>

                <Card className="bg-ivory-25">
                  <div className="flex items-center gap-3">
                    <Braces className="size-5 text-copper-700" />
                    <p className="font-display text-2xl text-ink-950">Masking indicators</p>
                  </div>
                  <div className="mt-4 flex flex-wrap gap-2">
                    {result.governance.maskedFields.length ? (
                      result.governance.maskedFields.map((field) => (
                        <Badge key={field} tone="warning">
                          {field}
                        </Badge>
                      ))
                    ) : (
                      <Badge tone="success">No masked fields in this view</Badge>
                    )}
                  </div>
                </Card>

                <JsonViewer value={cloudPayload} title="Cloud aggregate usage payload" height="h-72" />
              </div>
            ) : (
              <Card className="bg-ivory-25">Loading governance indicators...</Card>
            )}
          </Panel>
        </div>
      </section>

      {result ? <RelationshipJsonExplorer result={result} /> : null}

      <section className="grid gap-4 xl:grid-cols-[1fr_1fr]">
        <Panel eyebrow="Relationship map" title="How the lookup links source records">
          <div className="grid gap-3">
            {(result?.relationships ?? []).map((relationship) => (
              <Card key={relationship.relationshipId} className="bg-ivory-25">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div className="min-w-0">
                    <div className="flex flex-wrap gap-2">
                      <Badge tone={relationshipTone(relationship.linkKind)}>{relationship.linkKind}</Badge>
                      <Badge tone="neutral">{relationship.relationshipId}</Badge>
                    </div>
                    <h3 className="mt-3 break-words font-display text-2xl text-ink-950">
                      {labelize(relationship.relationshipType)}
                    </h3>
                    <p className="mt-2 break-words text-sm text-ink-600">
                      {relationship.sourceType} to {relationship.targetType}
                    </p>
                  </div>
                  <div className="flex flex-wrap gap-2">
                    <Badge tone="success">{formatConfidence(relationship.confidence)}</Badge>
                    <Badge tone="accent">Weight {formatConfidence(relationship.weight)}</Badge>
                  </div>
                </div>
                <p className="mt-3 text-sm leading-7 text-ink-700">{relationship.rationale}</p>
                <div className="mt-3">
                  <CitationChips citationIds={relationship.citationIds} tone="neutral" />
                </div>
              </Card>
            ))}
          </div>
        </Panel>

        <Panel eyebrow="Evidence pack" title="Local package metadata">
          {result ? (
            <div className="grid gap-4">
              <div className="grid gap-3 md:grid-cols-3">
                <Card className="bg-ivory-25">
                  <CalendarClock className="size-5 text-copper-700" />
                  <p className="mt-3 text-xs uppercase tracking-[0.18em] text-sage-700">Generated</p>
                  <p className="mt-2 text-sm leading-7 text-ink-800">
                    {formatDateTime(result.evidencePack.generatedAtUtc)}
                  </p>
                </Card>
                <Card className="bg-ivory-25">
                  <Clock3 className="size-5 text-sage-700" />
                  <p className="mt-3 text-xs uppercase tracking-[0.18em] text-sage-700">Version</p>
                  <p className="mt-2 break-words text-sm leading-7 text-ink-800">
                    {result.evidencePack.packageVersion}
                  </p>
                </Card>
                <Card className="bg-ivory-25">
                  <ShieldCheck className="size-5 text-gold-700" />
                  <p className="mt-3 text-xs uppercase tracking-[0.18em] text-sage-700">Evidence pack</p>
                  <p className="mt-2 break-words text-sm leading-7 text-ink-800">
                    {result.evidencePack.evidencePackId}
                  </p>
                </Card>
              </div>
              <JsonViewer
                value={safeJsonParse(result.evidencePack.localDerivedEvidencePackageJson, {})}
                title="Local derived evidence package"
                height="h-[420px]"
              />
            </div>
          ) : (
            <Card className="bg-ivory-25">Loading evidence pack metadata...</Card>
          )}
        </Panel>
      </section>
    </div>
  )
}
