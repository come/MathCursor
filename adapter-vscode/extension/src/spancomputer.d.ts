// Types pour le port JS réutilisé tel quel (parité verrouillée côté web-demo).
declare module '*/spancomputer.js' {
  interface Zone { start: number; end: number; }
  interface SpanComputerApi {
    computeZone(
      text: string,
      caret: number,
      omathRegions: Array<{ start: number; end: number }> | null,
      delims?: Set<string>
    ): Zone;
    SPAN_DELIMITERS: Set<string>;
    DEMO_DELIMITERS: Set<string>;
  }
  const api: SpanComputerApi;
  export default api;
}
