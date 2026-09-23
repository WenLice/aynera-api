import { StrictMode, useMemo } from "react";
import { createRoot } from "react-dom/client";
import { Amplify } from "aws-amplify";
import { ThemeProvider } from "@aws-amplify/ui-react";
import { FaceLivenessDetector } from "@aws-amplify/ui-react-liveness";
import "@aws-amplify/ui-react/styles.css";
import "./page.css";

/**
 * The face check. The app opens this page with ?session=&region=&pool= from POST /liveness/Start.
 *
 * The page streams the camera straight to AWS and then only says "done", "cancelled" or "error".
 * It never decides anything: the app then asks the API, which reads the verdict from AWS itself.
 * So everything it holds is safe to hold — a one-time session id and a Cognito pool id whose guest
 * role can do nothing but stream to a liveness session.
 */

declare global {
  interface Window {
    ReactNativeWebView?: { postMessage: (message: string) => void };
  }
}

type Status = "done" | "cancelled" | "error";

const params = new URLSearchParams(window.location.search);
const sessionId = params.get("session") ?? "";
const region = params.get("region") ?? "";
const identityPoolId = params.get("pool") ?? "";

if (identityPoolId) {
  Amplify.configure({ Auth: { Cognito: { identityPoolId, allowGuestAccess: true } } });
}

/** Tells whoever opened the page: the app's WebView on a phone, the parent window on the web. */
function notify(status: Status, detail?: string) {
  const message = JSON.stringify({ type: "liveness", status, sessionId, detail });
  window.ReactNativeWebView?.postMessage(message);
  if (window.parent !== window) window.parent.postMessage(message, "*");
}

function FaceCheck() {
  const ready = useMemo(() => Boolean(sessionId && region && identityPoolId), []);
  if (!ready) {
    return <p className="note">This page is opened from the Aynera app.</p>;
  }

  return (
    <FaceLivenessDetector
      sessionId={sessionId}
      region={region}
      onAnalysisComplete={async () => notify("done")}
      onUserCancel={() => notify("cancelled")}
      onError={(error) => notify("error", String(error.state))}
    />
  );
}

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <ThemeProvider>
      <main className="page">
        <FaceCheck />
      </main>
    </ThemeProvider>
  </StrictMode>,
);
