import { describe, expect, it } from 'vitest'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { runParityCheck, exportText, loadFromRepo, readFixture } from '../src/index.js'
import type { ContractParityInput } from '../src/index.js'

const currentDir = dirname(fileURLToPath(import.meta.url))
const passFixturePath = resolve(currentDir, '..', 'data', 'fixture-pass.json')
const failFixturePath = resolve(currentDir, '..', 'data', 'fixture-fail.json')

describe('contract parity checker', () => {
  it('passes deterministic parity fixtures without issues', () => {
    const report = runParityCheck(readFixture(passFixturePath))

    expect(report.isValid).toBe(true)
    expect(report.summary.issueCount).toBe(0)
    expect(exportText(report)).toContain('Scout contract parity: PASS')
  })

  it('reports missing fields, renamed fields, enum mismatches, and unsupported manifest features', () => {
    const report = runParityCheck(readFixture(failFixturePath))
    const messages = report.issues.map((issue) => issue.message)

    expect(report.isValid).toBe(false)
    expect(report.issues.map((issue) => issue.kind)).toEqual(
      expect.arrayContaining([
        'missing-field',
        'renamed-field',
        'enum-mismatch',
        'unsupported-manifest-feature',
      ]),
    )
    expect(messages).toEqual(
      expect.arrayContaining([
        expect.stringContaining("missing field 'provenanceJson'"),
        expect.stringContaining("rename 'sourceSelectorDefinitionId' to 'sourceSelectorId'"),
        expect.stringContaining('enum values differ'),
        expect.stringContaining("unsupported field 'oauth2Flows'"),
        expect.stringContaining("unsupported source type 'Warehouse'"),
        expect.stringContaining("unsupported capability 'BulkSync'"),
      ]),
    )
  })

  it('renders stable text report summaries', () => {
    const text = exportText(runParityCheck(readFixture(failFixturePath)))

    expect(text).toContain('Scout contract parity: FAIL')
    expect(text).toContain('Models compared: 2')
    expect(text).toContain('Enums compared: 2')
    expect(text).toContain('Manifests checked: 1')
    expect(text).toContain('[ERROR] enum-mismatch DataSourceKind')
  })

  it('classifies missing model warnings into actionable groups', () => {
    const report = runParityCheck(warningFixture())

    expect(report.isValid).toBe(true)
    expect(report.warningGroups.map((group) => group.category)).toEqual([
      'sdk-request-contract',
      'rest-transport-contract',
      'connector-authoring-contract',
    ])
    expect(report.warningGroups[0]?.issues[0]).toMatchObject({
      category: 'sdk-request-contract',
      model: 'UserContextLookupInput',
      sourceReference: 'src/KynticAI.Scout.Application/Contracts/Inputs.cs',
      targetReference: 'packages/typescript/scout-sdk/src/types.ts',
    })
  })

  it('renders grouped warnings with source and target references', () => {
    const text = exportText(runParityCheck(warningFixture()))

    expect(text).toContain('Warnings:')
    expect(text).toContain('SDK request contract gaps (1)')
    expect(text).toContain('REST transport DTO gaps (2)')
    expect(text).toContain('Connector authoring contract gaps (1)')
    expect(text).toContain('Action: Decide whether the SDK should expose these request/input models directly')
    expect(text).toContain(
      'References: source src/KynticAI.Scout.Application/Contracts/Inputs.cs; target packages/typescript/scout-sdk/src/types.ts',
    )
  })

  it('checks connector authoring enums against manifest validator constants', () => {
    const report = runParityCheck({
      restModels: [],
      graphQlModels: [],
      dotnetSdkModels: [],
      typescriptSdkModels: [],
      apiEnums: [
        {
          name: 'DataSourceKind',
          surface: 'rest',
          values: ['Crm'],
          sourceFile: 'src/KynticAI.Scout.Domain/Enums/DomainEnums.cs',
        },
        {
          name: 'ConnectorCapability',
          surface: 'connector-manifest',
          values: ['FetchSubject', 'Preview'],
          sourceFile: 'src/KynticAI.Scout.Application/Abstractions/IConnectorPlugin.cs',
        },
      ],
      dotnetSdkEnums: [],
      typescriptSdkEnums: [],
      connectorManifest: {
        allowedFields: ['connectorId', 'displayName', 'eventShape'],
        sourceTypes: ['Crm'],
        capabilities: ['FetchSubject'],
      },
      manifests: [],
    })

    expect(report.isValid).toBe(false)
    expect(report.issues).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          category: 'connector-authoring-contract',
          model: 'ConnectorCapability',
          field: 'capabilities',
        }),
      ]),
    )
    expect(report.issues.map((issue) => issue.message)).toEqual(
      expect.arrayContaining([
        expect.stringContaining('ConnectorCapability'),
      ]),
    )
  })

  it('loads assigned C# enum values for connector authoring parity checks', () => {
    const repoRoot = resolve(currentDir, '..', '..', '..', '..')
    const input = loadFromRepo(repoRoot)
    const dataSourceKind = input.apiEnums.find((shape) => shape.name === 'DataSourceKind')
    const connectorCapability = input.apiEnums.find((shape) => shape.name === 'ConnectorCapability')

    expect(dataSourceKind?.values).toEqual(['Crm', 'SqlMetric', 'EventStream', 'ProductUsage'])
    expect(connectorCapability?.values).toEqual([
      'FetchSubject',
      'Preview',
      'DryRun',
      'ScheduledSync',
      'EventTriggeredRecompute',
      'HealthCheck',
      'ConfigurationValidation',
      'SecureCredentialStorage',
      'UpgradeCompatibleCapture',
    ])
  })

  it('checks Score API OpenAPI schemas and TypeScript client paths', () => {
    const repoRoot = resolve(currentDir, '..', '..', '..', '..')
    const report = runParityCheck(loadFromRepo(repoRoot))

    expect(report.issues.filter((issue) => issue.category === 'score-api-contract')).toHaveLength(0)
  })

  it('reports Score API contract gaps', () => {
    const fixture = warningFixture()
    fixture.scoreApi = {
      sourceFile: 'schema/kyntic-score.openapi.yaml',
      paths: ['/v1/scores/investment'],
      schemas: ['InvestmentScoreRequest'],
      sdkClientPaths: [],
    }

    const report = runParityCheck(fixture)

    expect(report.isValid).toBe(false)
    expect(report.issues).toEqual(expect.arrayContaining([
      expect.objectContaining({
        kind: 'missing-score-contract',
        category: 'score-api-contract',
        model: '/v1/scores/credit',
      }),
      expect.objectContaining({
        kind: 'missing-score-contract',
        category: 'score-api-contract',
        model: 'CreditScore',
      }),
    ]))
  })
})

function warningFixture(): ContractParityInput {
  return {
    restModels: [
      {
        name: 'UserContextLookupInput',
        surface: 'rest',
        sourceFile: 'src/KynticAI.Scout.Application/Contracts/Inputs.cs',
        fields: [{ name: 'tenantSlug', type: 'string' }],
      },
      {
        name: 'V1ErrorBody',
        surface: 'rest',
        sourceFile: 'src/KynticAI.Scout.Api/Rest/VersionedRestContracts.cs',
        fields: [{ name: 'code', type: 'string' }],
      },
      {
        name: 'ConnectorHealthResult',
        surface: 'rest',
        sourceFile: 'src/KynticAI.Scout.Application/Contracts/ConnectorResults.cs',
        fields: [{ name: 'dataSourceId', type: 'Guid' }],
      },
    ],
    graphQlModels: [
      {
        name: 'V1ErrorBody',
        surface: 'graphql',
        sourceFile: 'src/KynticAI.Scout.Api/Rest/VersionedRestContracts.cs',
        fields: [{ name: 'code', type: 'string' }],
      },
    ],
    dotnetSdkModels: [
      {
        name: 'UserContextLookupInput',
        surface: 'dotnet-sdk',
        fields: [{ name: 'tenantSlug', type: 'string' }],
      },
      {
        name: 'V1ErrorBody',
        surface: 'dotnet-sdk',
        fields: [{ name: 'code', type: 'string' }],
      },
      {
        name: 'ConnectorHealthResult',
        surface: 'dotnet-sdk',
        fields: [{ name: 'dataSourceId', type: 'Guid' }],
      },
    ],
    typescriptSdkModels: [],
    apiEnums: [],
    dotnetSdkEnums: [],
    typescriptSdkEnums: [],
    connectorManifest: {
      allowedFields: [],
      sourceTypes: [],
      capabilities: [],
    },
    manifests: [],
  }
}
