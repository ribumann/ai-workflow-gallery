# Gallery integration package: data and permissions

The AI Workflow Gallery integration packages contain instructions, references and
generated icons. They do not contain catalog data, credentials, account tokens,
an OAuth connector, or a service that independently collects data.

Installing a skill teaches an assistant how to use the gallery. The assistant
provider processes your prompts and any content you choose to share under that
provider's policies. Browser access and MCP access are separate permissions:
a package download grants neither. Complete sign-in and consent yourself; never
provide session credentials to an assistant.

The public gallery exposes published examples without sign-in. Playground content,
including uploaded files, is shared, public and disposable. Do not place private,
confidential, customer-identifiable or sensitive information there. A deployed
gallery can use service monitoring and request/error logs; package installation
does not disable that monitoring or the assistant provider's processing.

An organization's private deployment has its own access, retention, monitoring
and data policies. Its administrator must review the package, required account
entitlements and provider terms before distributing or importing it. This notice
does not establish organizational approval or guarantee compatibility.

The package source is offered under the repository's MIT license. That license
does not grant access to a gallery deployment or replace third-party service terms.
