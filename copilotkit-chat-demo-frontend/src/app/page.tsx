"use client";

import { CopilotKit, useCopilotAction } from "@copilotkit/react-core";
import { CopilotChat } from "@copilotkit/react-ui";
import { runtimeUrl } from "@/lib/copilot-config";
import "@copilotkit/react-ui/styles.css";
import { WeatherCard } from "@/components/WeatherCard";
import { useState } from "react";

export default function Home() {
  return (
    <CopilotKit
      runtimeUrl={runtimeUrl}
      publicApiKey={process.env.NEXT_PUBLIC_COPILOTKIT_PUBLIC_API_KEY ?? "demo-placeholder"}
    >
      <MainContent />
    </CopilotKit>
  );
}

function MainContent() {
  const [weatherData, setWeatherData] = useState<any>(null);

  // We register the action on the frontend as well to handle the UI rendering.
  // Even if the backend executes the logic, the frontend can intercept the intent to render the card.
  // However, since we are using "Backend Actions" via the C# loop, the frontend might not see this as a frontend action.
  // So we will use a hybrid approach: The backend returns a structured response or we use a client-side action that calls the backend.
  // For this demo, to strictly follow "Backend Actions", we rely on the backend tool. 
  // But to show "Generative UI", we'll use useCopilotAction which is the standard way to render UI in CopilotKit.

  // Removed useCopilotAction for show_weather_ui as per user request to stick to text.

  return (
    <main className="flex min-h-screen flex-col items-center justify-center p-4 md:p-24 relative overflow-hidden">
      {/* Background Blobs */}
      <div className="absolute top-0 left-0 w-96 h-96 bg-purple-500/30 rounded-full mix-blend-multiply filter blur-3xl opacity-70 animate-blob"></div>
      <div className="absolute top-0 right-0 w-96 h-96 bg-blue-500/30 rounded-full mix-blend-multiply filter blur-3xl opacity-70 animate-blob animation-delay-2000"></div>
      <div className="absolute -bottom-8 left-20 w-96 h-96 bg-pink-500/30 rounded-full mix-blend-multiply filter blur-3xl opacity-70 animate-blob animation-delay-4000"></div>

      <section className="z-10 w-full max-w-5xl items-center justify-between font-mono text-sm lg:flex flex-col gap-8">
        <div className="text-center mb-8">
          <h1 className="text-5xl font-extrabold text-transparent bg-clip-text bg-gradient-to-r from-blue-400 to-purple-600 mb-4">
            CopilotKit + C#
          </h1>
          <p className="text-lg text-blue-100/80 max-w-2xl mx-auto">
            Experience the power of Agentic AI with a .NET backend.
            Ask about the weather to see the magic happen.
          </p>
        </div>

        <div className="w-full h-[600px] glass-card relative flex flex-col overflow-hidden">
          <CopilotChat
            className="h-full"
            labels={{
              title: "Agentic Assistant",
              initial: "Hi! I can help you with weather and more. Try 'What's the weather in Tokyo?'",
            }}
            instructions="You are a helpful assistant. When asked about weather, use the 'get_weather' tool to fetch data. Provide a detailed text response with the weather information."
          />
        </div>
      </section>
    </main>
  );
}
