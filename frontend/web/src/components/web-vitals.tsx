"use client";

import { useReportWebVitals } from "next/web-vitals";

const REPORTED = new Set(["LCP", "INP", "FCP", "TTFB", "CLS"]);

/**
 * Manda os Web Vitals de cada página ao BFF (/api/vitals) por sendBeacon: sai mesmo quando a pessoa
 * fecha a aba, sem atrasar nada. Não renderiza nada.
 */
export function WebVitals() {
  useReportWebVitals((metric) => {
    if (!REPORTED.has(metric.name)) return;
    const body = JSON.stringify({
      name: metric.name,
      value: metric.value,
      rating: metric.rating,
      // a página de quando a métrica fechou (o CLS e o INP fecham ao sair dela)
      path: window.location.pathname,
    });
    if (!navigator.sendBeacon?.("/api/vitals", body)) {
      void fetch("/api/vitals", { method: "POST", body, keepalive: true }).catch(() => {});
    }
  });
  return null;
}
