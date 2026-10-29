# Shuttle Dashboard

Vue/Vite frontend for the Fleet Manager dashboard.

## Requirements

- Node 22 or newer.

## Build

```bash
npm ci
npm run build
```

The build writes static assets into `../FleetManager/wwwroot`, which the Fleet
Manager serves from the same origin.

## Run locally

1. Build the dashboard:

   ```bash
   npm ci
   npm run build
   ```

2. Start the Fleet Manager from the repository root:

   ```bash
   export SHUTTLE_DB_CONNECTION_STRING='<local PostgreSQL connection string>'
   dotnet run --project src/FleetManager -- serve
   ```

3. Open `http://127.0.0.1:5080`.
