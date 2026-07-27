// The dev harness: the REAL console against real services. Point it at one app with
// `?base=http://localhost:5xxx`, or serve `console.config.json` next to the console to
// drive a whole registry.
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "./console.css";
import { ConsoleApp } from "../src/ConsoleApp";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <ConsoleApp />
  </StrictMode>,
);
