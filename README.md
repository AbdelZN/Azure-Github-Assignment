# Azure / GitHub Assignment – Image generation with weather overlay

This project is an Azure Functions app that generates images with current weather data on top of them.
The generation runs in the background using Azure Storage Queues and stores the resulting images in Blob Storage.

## Architecture

1. HTTP start endpoint creates a new job and returns a `jobId`.
2. A message is written to `queue-start`.
3. Fan-out function (QueueTrigger on `queue-start`) fetches station measurements from Buienradar and creates 50 station jobs on `queue-process`.
4. Station processor (QueueTrigger on `queue-process`) downloads a public image, writes weather data on it, and uploads the final image to Blob Storage under `images/{jobId}/...`.
5. HTTP status endpoint returns progress.
6. HTTP results endpoint returns a list of generated image URLs from Blob Storage.

## Endpoints

Local base URL:
- http://localhost:7071

Azure base URL:
- https://ccd-func-otpo3ptm46ydu.azurewebsites.net

### Start a job
- Method: POST
- Path: /api/StartImageSet
- Response: 202 Accepted
- Body example:
  { "jobId": "..." }

### Get status
- Method: GET
- Path: /api/GetStatus?jobId={jobId}
- Response example:
  { "jobId": "...", "status": "running|completed", "total": 50, "done": 0-50, "failed": 0 }

### Get results
- Method: GET
- Path: /api/GetResults?jobId={jobId}
- Response example:
  { "jobId": "...", "results": [ "https://.../images/{jobId}/station-....jpg", ... ] }

## HTTP files (API documentation)

Request examples are included in:
- docs/http/01-start.http
- docs/http/02-status.http
- docs/http/03-results.http

## Data sources

- Weather data (Buienradar JSON feed):
  https://data.buienradar.nl/2.0/feed/json
- Public image source:
  A public image endpoint is used to retrieve images during processing.

Note:
The Buienradar feed contains ~40 station measurement entries in `actual.stationmeasurements`.
To match the assignment requirement of 50 station jobs, the app pads/repeats station jobs to produce 50 processing jobs.

## Local development

Prerequisites:
- .NET SDK (project target)
- Azure Functions Core Tools
- Azurite (Queues + Blobs) OR an Azure Storage connection string

Steps:
1. Start Azurite.
2. Ensure `local.settings.json` contains:
   - AzureWebJobsStorage = UseDevelopmentStorage=true
   - FUNCTIONS_WORKER_RUNTIME = dotnet-isolated
3. Run the Functions app:
   - func start
4. Call StartImageSet, then poll GetStatus until completed, then call GetResults.

## Azure deployment

Infrastructure is defined in:
- infra/main.bicep

Deployment script:
- deploy.ps1

Prerequisites:
- Azure CLI (az) + Bicep
- az login

Deploy:
- Run from repo root:
  .\deploy.ps1

The script:
1. Creates/updates Azure resources with Bicep (Storage + Queues + Function App).
2. Publishes the Functions app using dotnet publish.
3. Deploys using Azure CLI zip deployment.

## Repository contents

- src/Functions/          Azure Functions (.NET isolated)
- infra/main.bicep        Infrastructure (includes queues + blob container)
- deploy.ps1              Publish + provision + deploy script
- docs/http/              HTTP files for API documentation

## Requirements mapping (Must)

- Public HTTP API to start image generation: StartImageSet
- QueueTrigger background processing: FanOut + ProcessStation
- Blob Storage to store and expose generated images
- Buienradar API used for station measurement data
- Public image source used for image retrieval
- Public HTTP API to fetch generated images: GetResults
- HTTP files as API documentation: docs/http
- Bicep template including queues: infra/main.bicep
- deploy.ps1 using dotnet CLI + Azure CLI: deploy.ps1
- Deployed to Azure and working endpoints
