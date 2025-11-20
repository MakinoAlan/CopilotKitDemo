// copilotkit-chat-demo-frontend/src/lib/copilot-config.ts
export const runtimeUrl =
  // Default to local ASP.NET runtime on HTTP to avoid dev-cert friction; override with NEXT_PUBLIC_COPILOTKIT_RUNTIME_URL if needed.
  process.env.NEXT_PUBLIC_COPILOTKIT_RUNTIME_URL ?? "http://localhost:5000/copilotkit";
