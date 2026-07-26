// The dev harness: the REAL console against a real service. Point it at a running app
// with `?base=http://localhost:5xxx` (CorPay's api head) or serve it from the app itself.
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "./console.css";
import { Console } from "../src/Console";

const base = new URLSearchParams(window.location.search).get("base") ?? "";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <Console baseUrl={base} title={base || "same-origin"} />
  </StrictMode>,
);
