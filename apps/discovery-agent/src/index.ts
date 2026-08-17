#!/usr/bin/env node
import { pathToFileURL } from 'node:url'
import { runCli } from './cli.js'

if (process.argv[1] !== undefined && import.meta.url === pathToFileURL(process.argv[1]).href) {
  await runCli(process.argv.slice(2))
}

export { auditCodebase, runThreeTierAudit } from './audit.js'
export { generateHandover, renderHandoverMarkdown, toHandoverDocument } from './handover.js'
export { createDiscoveryAgentServer, getDiscoveryStatus } from './server.js'
export type * from './types.js'
