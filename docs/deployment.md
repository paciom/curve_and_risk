# Deployment

What exists is a container image and the pipeline that builds, tests, scans and publishes it. Nothing deploys the image to a cloud yet; that needs an account and credentials this repository does not have.

## The image

[`Dockerfile`](../Dockerfile) builds the API and its web page in two stages: the .NET SDK to publish, the ASP.NET runtime to run. It listens on port 8080 and runs as an unprivileged user.

```bash
docker build -t curverisk-api .
```

```bash
docker run --rm -p 8080:8080 -v curverisk-data:/data curverisk-api
```

Then open `http://localhost:8080`.

## Configuration

Set as environment variables on the container.

| Variable | Purpose | Default |
|---|---|---|
| `Database__Provider` | `Sqlite` or `Postgres` | `Sqlite` |
| `ConnectionStrings__CurveRisk` | Database connection string | A SQLite file on the `/data` volume |
| `Database__CreateOnStart` | Create the schema on start | `true` for SQLite, `false` for PostgreSQL |
| `ZAI_API_KEY` with `COPILOT_MODEL`, or `ANTHROPIC_API_KEY` | Enables the Risk Copilot | Unset: the Copilot endpoint answers 503 |
| `Copilot__DailyBudgetUsd` | Daily spend cap for the Copilot | `5` |

Secrets are never built into the image. Supply them from the host's secret store.

With PostgreSQL there are no versioned migrations yet, so for now a first deployment sets `Database__CreateOnStart=true` once. That is acceptable for a demo and not for a system that will change its schema with data in it.

## The pipeline

[`container.yml`](../.github/workflows/container.yml) runs on pull requests that touch the image's inputs and on every push to `main`.

| Step | Fails when |
|---|---|
| Build | The image does not build |
| Smoke test | The running container does not answer `/health`, serve the page, calibrate a curve through the API, or it runs as root |
| Vulnerability scan | Trivy finds a fixable critical or high vulnerability in the image |
| Publish (main only) | Pushes `ghcr.io/<owner>/<repo>` tagged with the commit and `latest`, with provenance and an SBOM attached |

An image is published only after the full quality gate has passed for the same commit, if `quality-gate` and `image` are both required checks on `main`.

## What a cloud deployment would add

Deploying to Azure Container Apps, the target in the plan, needs three things that are decisions and credentials, not code:

1. An Azure subscription, a resource group and a Container Apps environment.
2. A federated credential so GitHub Actions can sign in with OIDC, with no stored password. Its client, tenant and subscription ids go in as repository secrets.
3. A managed PostgreSQL database and its connection string, held as a Container Apps secret.

With those in place the deployment is one more job after `image`: sign in with `azure/login`, then `az containerapp update --image ghcr.io/<owner>/<repo>:sha-<commit>`, behind a GitHub environment with a required reviewer so a person approves each production release.

## Not verified

There is no Docker on the machine this was written on. The Dockerfile and the workflow have been checked for syntax only; the first run on GitHub is their first real test.
