# CopilotKitDemo

This repository contains a minimal CopilotKit chat demo with a .NET 8 backend and a Next.js 14 frontend. Use the steps below to confirm the project runs end-to-end on a machine with internet access.

## Prerequisites
- .NET 8 SDK (`dotnet --version` should report 8.x)
- Node.js 18+ and npm
- Network access to restore NuGet and npm packages

## Backend: CopilotKitChatDemo.Api
1. Navigate to `CopilotKitChatDemo.Api`.
2. Restore and build the API:
   ```bash
   dotnet restore
   dotnet build
   ```
3. Run the API locally (listens on port 5001 by default):
   ```bash
   dotnet run
   ```

## Frontend: copilotkit-chat-demo-frontend
1. Navigate to `copilotkit-chat-demo-frontend`.
2. Install dependencies and start the Next.js dev server:
   ```bash
   npm install
   npm run dev
   ```
3. Ensure the frontend can reach the backend by setting `NEXT_PUBLIC_COPILOTKIT_RUNTIME_URL` if the API is not on the default `http://localhost:5001/copilotkit`.

## Notes on limited environments
If package installs fail (e.g., 403 responses or missing SDKs), install the prerequisites or run the project on a machine with network access. Once the dependencies restore successfully, follow the commands above to verify the application builds and runs.
