# KynticAI Scout — Documentation Site

Public documentation site for [KynticAI Scout](https://github.com/PaulJMaddison/scout), built with [Astro Starlight](https://starlight.astro.build/).

Workspace naming source of truth: [`../docs/source-of-truth-naming-map.md`](../docs/source-of-truth-naming-map.md).

## Prerequisites

- **Node.js 22+** (or use the repo-local Node installed by `scripts/ensure-node.sh`)
- **npm 10+**

## Local Development

```bash
cd docs-site
npm install
npm run dev
```

The dev server starts at [http://localhost:4321](http://localhost:4321) with hot-reload.

## Build

```bash
npm run build
```

Static output is written to `docs-site/dist/`. Preview the built site with:

```bash
npm run preview
```

This project intentionally does not contain GitHub Pages workflows, CI/CD,
release automation, deployment automation, or hosting scripts. It is a
locally buildable documentation site ready to be mounted by the approved
KynticAI website path later.

## Project Structure

```
docs-site/
├── astro.config.mjs          # Starlight configuration and sidebar
├── package.json
├── public/                    # Static assets (favicon, images)
├── src/
│   ├── content/
│   │   └── docs/              # Documentation pages (Markdown / MDX)
│   │       ├── index.mdx      # Landing page
│   │       ├── architecture.md
│   │       ├── schema-reference.md
│   │       ├── self-hosting.md
│   │       ├── getting-started/
│   │       │   ├── what-is-scout.md
│   │       │   ├── installation.md
│   │       │   └── quickstart.md
│   │       ├── apis/
│   │       │   ├── overview.md
│   │       │   ├── graphql.md
│   │       │   └── rest.md
│   │       ├── sdks/
│   │       │   ├── overview.md
│   │       │   ├── typescript.md
│   │       │   └── dotnet.md
│   │       ├── connectors/
│   │       │   └── authoring.md
│   │       └── concepts/
│   │           ├── connector-basics.md
│   │           └── open-source-vs-enterprise.md
│   ├── content.config.ts      # Astro content collection config
│   └── styles/
│       └── custom.css         # KynticAI brand overrides (Aged Book palette)
└── tsconfig.json
```

## Adding Pages

1. Create a new `.md` or `.mdx` file under `src/content/docs/`.
2. Add YAML frontmatter with at least `title` and `description`.
3. Register the page in the `sidebar` array in `astro.config.mjs`.

## Brand Guidelines

- Use **KynticAI** (with `AI`) for the public brand.
- Product tier: **KynticAI Scout** (open core) / scoped private Enterprise modules / optional Cloud control plane.
- Use British English for all user-facing copy.
- Colour palette follows the Aged Book / Sovereign Rust direction — see `src/styles/custom.css`.
- Public images must use approved logo files from `docs/images/brand`; the
  docs favicon uses the approved logo mark copied into `public/brand/`.
