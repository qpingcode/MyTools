/**
 * Development-time helpers for MyTools plugins.
 *
 * This entry point is intended for build and watch scripts, not plugin runtime code.
 */
import { requestDevelopmentPluginRefreshWithOptions } from "./developmentRefresh.ts";

/**
 * Notifies a running MyTools instance that a development plugin was rebuilt.
 * The request is retried once after a short delay when the pipe is not yet available.
 */
export function requestDevelopmentPluginRefresh(pluginId: string): Promise<void> {
  return requestDevelopmentPluginRefreshWithOptions(pluginId);
}
