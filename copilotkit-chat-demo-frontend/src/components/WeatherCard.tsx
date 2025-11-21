import React from "react";

interface WeatherCardProps {
  location: string;
  temperature: number;
  conditions: string;
  wind_speed: string;
  humidity: string;
}

export function WeatherCard(props: WeatherCardProps) {
  const { location, temperature, conditions, wind_speed, humidity } = props;
  console.log("WeatherCard props:", props);

  const safeConditions = conditions || "Sunny"; // Default to Sunny if undefined to prevent crash

  return (
    <div className="p-6 rounded-2xl bg-slate-900/90 backdrop-blur-xl border border-white/10 shadow-xl max-w-sm w-full mx-auto transition-all hover:scale-105 duration-300">
      <div className="flex justify-between items-start mb-4">
        <div>
          <h2 className="text-2xl font-bold text-white mb-1">{location}</h2>
          <p className="text-blue-200 text-sm font-medium">{new Date().toLocaleDateString('en-US', { weekday: 'long', month: 'long', day: 'numeric' })}</p>
        </div>
        <div className="bg-white/10 p-2 rounded-lg">
          {/* Simple Icon Placeholder based on conditions */}
          {safeConditions.toLowerCase().includes("sun") ? (
            <span className="text-2xl">☀️</span>
          ) : safeConditions.toLowerCase().includes("cloud") ? (
            <span className="text-2xl">☁️</span>
          ) : (
            <span className="text-2xl">🌤️</span>
          )}
        </div>
      </div>

      <div className="flex items-center justify-center py-4">
        <span className="text-6xl font-bold text-transparent bg-clip-text bg-gradient-to-b from-white to-white/60">
          {temperature}°
        </span>
      </div>

      <div className="text-center mb-6">
        <p className="text-xl font-medium text-white">{safeConditions}</p>
      </div>

      <div className="grid grid-cols-2 gap-4">
        <div className="bg-white/5 rounded-xl p-3 text-center backdrop-blur-sm border border-white/5">
          <p className="text-blue-200 text-xs uppercase tracking-wider mb-1">Wind</p>
          <p className="text-white font-semibold">{wind_speed}</p>
        </div>
        <div className="bg-white/5 rounded-xl p-3 text-center backdrop-blur-sm border border-white/5">
          <p className="text-blue-200 text-xs uppercase tracking-wider mb-1">Humidity</p>
          <p className="text-white font-semibold">{humidity}</p>
        </div>
      </div>
    </div>
  );
}
