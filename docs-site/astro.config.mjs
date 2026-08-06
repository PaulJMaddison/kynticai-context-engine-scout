// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

const structuredData = JSON.stringify([
	{
		'@context': 'https://schema.org',
		'@type': 'SoftwareApplication',
		name: 'KynticAI Scout',
		applicationCategory: 'DeveloperApplication',
		operatingSystem: 'Self-hosted',
		softwareVersion: '2.9.0',
		license: 'https://opensource.org/licenses/MIT',
		url: 'https://kynticai.com',
		description:
			'Open-source context infrastructure for AI-enabled products. Scout exposes governed semantic context through GraphQL, REST, and typed SDKs.',
	},
	{
		'@context': 'https://schema.org',
		'@type': 'Article',
		headline: 'KynticAI Scout technical documentation',
		inLanguage: 'en-GB',
		about: {
			'@type': 'SoftwareApplication',
			name: 'KynticAI Scout',
		},
		publisher: {
			'@type': 'Organization',
			name: 'KynticAI',
		},
	},
]);

// https://astro.build/config
export default defineConfig({
	site: 'https://kynticai.com',
	integrations: [
		starlight({
			title: 'KynticAI Scout',
			description:
				'Documentation for KynticAI Scout — open-source context infrastructure for AI-enabled products.',
			favicon: '/brand/kynticai-logo-mark.png',
			social: [
				{
					icon: 'github',
					label: 'GitHub',
					href: 'https://github.com/PaulJMaddison/scout',
				},
			],
			editLink: {
				baseUrl: 'https://github.com/PaulJMaddison/scout/edit/main/docs-site/',
			},
			head: [
				{ tag: 'meta', attrs: { property: 'og:type', content: 'article' } },
				{ tag: 'meta', attrs: { property: 'og:site_name', content: 'KynticAI Scout Documentation' } },
				{
					tag: 'meta',
					attrs: {
						property: 'og:description',
						content: 'Technical documentation for the KynticAI Scout open-source data plane.',
					},
				},
				{
					tag: 'meta',
					attrs: { property: 'og:image', content: 'https://kynticai.com/brand/kynticai-logo-mark.png' },
				},
				{ tag: 'meta', attrs: { name: 'twitter:card', content: 'summary' } },
				{ tag: 'meta', attrs: { name: 'twitter:title', content: 'KynticAI Scout Documentation' } },
				{
					tag: 'meta',
					attrs: {
						name: 'twitter:description',
						content: 'Technical documentation for the KynticAI Scout open-source data plane.',
					},
				},
				{
					tag: 'meta',
					attrs: { name: 'twitter:image', content: 'https://kynticai.com/brand/kynticai-logo-mark.png' },
				},
				{
					tag: 'script',
					attrs: { type: 'application/ld+json' },
					content: structuredData,
				},
			],
			customCss: ['./src/styles/custom.css'],
			sidebar: [
				{
					label: 'Getting Started',
					items: [
						{ label: 'Quickstart', slug: 'getting-started/quickstart' },
						{ label: 'What is KynticAI Scout?', slug: 'getting-started/what-is-scout' },
						{ label: 'Installation', slug: 'getting-started/installation' },
					],
				},
				{
					label: 'Core Concepts',
					items: [
						{ label: 'Architecture', slug: 'architecture' },
						{ label: 'Schema Reference', slug: 'schema-reference' },
						{ label: 'Open Source vs Enterprise', slug: 'concepts/open-source-vs-enterprise' },
					],
				},
				{
					label: 'APIs',
					items: [
						{ label: 'API Overview', slug: 'apis/overview' },
						{ label: 'GraphQL API', slug: 'apis/graphql' },
						{ label: 'REST API', slug: 'apis/rest' },
						{ label: 'Score API Contract', slug: 'apis/score-api' },
					],
				},
				{
					label: 'SDKs',
					items: [
						{ label: 'SDK Overview', slug: 'sdks/overview' },
						{ label: 'TypeScript', slug: 'sdks/typescript' },
						{ label: '.NET', slug: 'sdks/dotnet' },
					],
				},
				{
					label: 'Operations',
					items: [
						{ label: 'Connector Authoring Guide', slug: 'connectors/authoring' },
						{ label: 'Connector Basics', slug: 'concepts/connector-basics' },
						{ label: 'Discovery Agent', slug: 'operations/discovery-agent' },
						{ label: 'n8n Node', slug: 'operations/n8n-node' },
						{ label: 'Self-Hosting', slug: 'self-hosting' },
					],
				},
			],
		}),
	],
});
