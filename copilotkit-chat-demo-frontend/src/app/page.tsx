// copilotkit-chat-demo-frontend/src/app/page.tsx
"use client";

import { CopilotKit } from "@copilotkit/react-core";
import { CopilotChat } from "@copilotkit/react-ui";
import { runtimeUrl } from "@/lib/copilot-config";

export default function Home() {
  return (
    <CopilotKit
      runtimeUrl={runtimeUrl}
      // This tells CopilotKit where to send chat + tool calls. Our ASP.NET API implements
      // a compatible runtime endpoint at /copilotkit.
      // For Copilot Cloud, set NEXT_PUBLIC_COPILOTKIT_PUBLIC_API_KEY and omit runtimeUrl.
      publicApiKey={process.env.NEXT_PUBLIC_COPILOTKIT_PUBLIC_API_KEY ?? "demo-placeholder"}
    >
      <section className="card">
        <div className="header">
          <div>
            <h1>CopilotKit + ASP.NET Core</h1>
            <p className="subtitle">
              Minimal end-to-end example: Next.js frontend → CopilotKit provider → ASP.NET
              backend → OpenAI.
            </p>
          </div>
          <span className="badge">App Router • TypeScript</span>
        </div>

        <div className="chat-container">
          <CopilotChat
            className="copilotkit-chat"
            labels={{
              title: "Ask the demo bot",
              initial: "Say hello to your CopilotKit + C# backend bot!",
            }}
            // You can pre-seed the assistant with a description so the chat starts helpful.
            instructions="You are a friendly assistant showcasing how CopilotKit can talk to a C# backend."
          />
        </div>
      </section>
    </CopilotKit>
  );
}
