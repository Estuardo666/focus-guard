import assert from "node:assert/strict";
import test from "node:test";
import { createNativePortClient } from "../src/native-port-client.js";

class FakeEvent<T> {
  private listener?: (value: T) => void;
  addListener(listener: (value: T) => void) { this.listener = listener; }
  emit(value: T) { this.listener?.(value); }
}

class FakePort {
  readonly onMessage = new FakeEvent<any>();
  readonly onDisconnect = new FakeEvent<void>();
  readonly requests: any[] = [];
  postMessage(request: any) { this.requests.push(request); }
}

test("persistent native port fails closed immediately and reconnects", async () => {
  const ports: FakePort[] = [];
  const unavailable: string[] = [];
  let client: ReturnType<typeof createNativePortClient>;
  let reconnectQuery: Promise<any> | undefined;
  const browserApi = {
    runtime: {
      connectNative() {
        const port = new FakePort();
        ports.push(port);
        return port;
      }
    }
  };
  client = createNativePortClient({
    browserApi,
    hostName: "com.focusguard.native",
    markUnavailable: (errorCode: string) => unavailable.push(errorCode),
    onReconnect: () => {
      reconnectQuery = client.query({ correlationId: "second" });
    },
    responseTimeoutMilliseconds: 100,
    reconnectDelayMilliseconds: 5
  });

  const first = client.query({ correlationId: "first" });
  assert.equal(ports.length, 1);
  ports[0].onMessage.emit({ correlationId: "first", serviceReady: true });
  assert.equal((await first).serviceReady, true);

  const pending = client.query({ correlationId: "pending" });
  ports[0].onDisconnect.emit(undefined);
  await assert.rejects(pending, /nativeHostDisconnected/);
  assert.deepEqual(unavailable, ["nativeHostDisconnected"]);

  await new Promise((resolve) => setTimeout(resolve, 15));
  assert.equal(ports.length, 2);
  ports[1].onMessage.emit({ correlationId: "second", serviceReady: true });
  assert.equal((await reconnectQuery!).serviceReady, true);
});
