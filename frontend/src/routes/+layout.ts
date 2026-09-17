// Built static and served by the hub on one origin (ADR-0002). The surfaces read live data at
// run time, so nothing is prerendered.
export const prerender = false;
export const ssr = false;
