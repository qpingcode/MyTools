/**
 * Node-side named-pipe transport for the v3 message bus. Connects to the host's pipe server
 * (created by NamedPipeTransport on the C# side), runs an incremental FrameDecoder read loop,
 * and sends length-prefixed frames. Mirrors MyTools.Host.Transports.NamedPipeTransport.
 */

import { connect as netConnect, type Socket } from "node:net";
import { encodeFrameString } from "./framing.ts";
import { canonicalStringify, type Envelope } from "./protocol.ts";

type MessageHandler = (env: Envelope) => void;
type DisconnectHandler = () => void;

export class NodeTransport {
  private socket: Socket | null = null;
  private messageHandlers = new Set<MessageHandler>();
  private disconnectHandlers = new Set<DisconnectHandler>();
  private closed = false;

  onMessage(handler: MessageHandler): () => void {
    this.messageHandlers.add(handler);
    return () => { this.messageHandlers.delete(handler); };
  }

  onDisconnect(handler: DisconnectHandler): void {
    this.disconnectHandlers.add(handler);
  }

  get isConnected(): boolean {
    return this.socket !== null && !this.closed;
  }

  /** Connects to a Windows named pipe (\\.\pipe\<name>). */
  async connect(pipePath: string): Promise<void> {
    this.socket = (netConnect as any)(pipePath) as Socket;

    await new Promise<void>((resolve, reject) => {
      this.socket!.once("connect", () => resolve());
      this.socket!.once("error", (err: Error) => reject(err));
    });

    // Single-writer ordering is guaranteed by the socket itself; a dedicated read loop decodes
    // frames incrementally and dispatches each complete envelope.
    const { FrameDecoder } = await import("./framing.ts");
    const decoder = new FrameDecoder();

    this.socket.on("data", (chunk: Buffer) => {
      let result = decoder.feed(chunk);
      if (result.isFatal) {
        this.handleDisconnect();
        return;
      }
      while (result.hasFrame) {
        try {
          const env = JSON.parse(result.payload.toString("utf8")) as Envelope;
          for (const h of this.messageHandlers) h(env);
        } catch {
          // Illegal JSON closes the connection per design.
          this.handleDisconnect();
          return;
        }
        result = decoder.feed(Buffer.alloc(0));
        if (result.isFatal) {
          this.handleDisconnect();
          return;
        }
      }
    });

    this.socket.on("close", () => this.handleDisconnect());
    this.socket.on("error", () => this.handleDisconnect());
  }

  /** Serializes an envelope to a length-prefixed frame and writes it. */
  send(env: Envelope): void {
    if (!this.socket || this.closed) {
      throw new Error("transport is not connected");
    }
    this.socket.write(encodeFrameString(canonicalStringify(env)));
  }

  async close(): Promise<void> {
    this.closed = true;
    if (this.socket) {
      this.socket.end();
      await new Promise<void>((resolve) => {
        if (this.socket!.destroyed) return resolve();
        this.socket!.once("close", () => resolve());
      });
      this.socket = null;
    }
  }

  private handleDisconnect(): void {
    if (this.closed) return;
    this.closed = true;
    for (const h of this.disconnectHandlers) h();
  }
}
