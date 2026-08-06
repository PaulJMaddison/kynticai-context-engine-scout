# Pilot Lead Capture

The KynticAI Scout web console includes a `/pilot` landing page with a
"Request a paid pilot scope" form. Operators can point that form at their own
lead-capture endpoint with the `VITE_PILOT_LEAD_ENDPOINT` build-time variable,
or leave it unset to use the safe default (a prefilled email).

This page is for operators who self-host or build the web app and want to
enable the lead form. It is optional and off by default.

## What the variable does

`VITE_PILOT_LEAD_ENDPOINT` is a Vite environment variable read at build time:

- `apps/web/src/features/marketing/site-constants.ts` resolves it into the
  `pilotLeadEndpoint` constant.
- `apps/web/src/features/marketing/pilot-page.tsx` (the `/pilot` page) uses it
  in the form's submit handler.

When `VITE_PILOT_LEAD_ENDPOINT` is set to a non-empty URL, submitting the form
sends a `POST` request with a JSON body to that URL. When it is unset or empty,
submitting the form opens a prefilled email to `VITE_PILOT_CONTACT_EMAIL`
(default `paul@kynticai.com`) instead. If the endpoint is set but rejects the
submission, the form shows a failure message and then opens the prefilled
email fallback.

The safe default is **unset**: production build examples leave
`VITE_PILOT_LEAD_ENDPOINT` off or commented out so no lead data leaves the
site until an operator deliberately configures an endpoint.

## Setup

The variable is baked into the web build, so set it before building:

```powershell
# apps/web/.env.local (local dev) or your build environment
VITE_PILOT_LEAD_ENDPOINT=https://<your-domain>/api/v1/crm/leads
VITE_PILOT_CONTACT_EMAIL=you@example.com
```

The Docker build accepts it as a build argument:

```dockerfile
ARG VITE_PILOT_LEAD_ENDPOINT=
ENV VITE_PILOT_LEAD_ENDPOINT=${VITE_PILOT_LEAD_ENDPOINT}
```

## Data format expected at the endpoint

The form issues a single unauthenticated `POST` with
`Content-Type: application/json`. The endpoint should accept a 2xx response to
a body shaped like:

```json
{
  "name": "Jane Smith",
  "workEmail": "jane@example.com",
  "company": "Example Ltd",
  "sourceSystems": "PostgreSQL, CRM, CSV exports",
  "targetWorkflow": "Renewal risk workflow",
  "submissionSource": "Website",
  "website": "",
  "spamChallengeToken": "<cloudflare-turnstile-token-or-empty>",
  "utmSource": "paid_search",
  "utmMedium": "cpc",
  "utmCampaign": "scout-pilot",
  "utmTerm": "",
  "utmContent": "",
  "referrer": "https://example.com",
  "landingPagePath": "/pilot"
}
```

Field notes:

- `name`, `workEmail`, `company`, `sourceSystems`, and `targetWorkflow` are
  the form fields.
- `submissionSource` is always `"Website"`.
- `website` is a hidden honeypot field; it should stay empty.
- `spamChallengeToken` carries the Cloudflare Turnstile token when
  `VITE_TURNSTILE_SITE_KEY` is configured, and is an empty string otherwise.
- The `utm*`, `referrer`, and `landingPagePath` fields are attribution
  metadata read from the visitor's URL and referrer.

The endpoint should treat the body as untrusted input and apply its own
validation, rate limiting, and abuse protection.

## Privacy

Lead capture collects personal and marketing data (name, work email, company,
attribution metadata) and must follow the same consent and lawful-basis
requirements as the rest of the public site. Review
[`docs/legal/cookie-and-event-consent-draft.md`](legal/cookie-and-event-consent-draft.md)
before enabling it in production; the draft covers the default position,
consent requirements, and the boundary between public-site events and customer
operational data. Do not submit raw customer operational data, credentials,
secrets, or documents through the form.
