import { readFile } from "node:fs/promises";
import { JSDOM } from "jsdom";
import { describe, expect, it } from "vitest";

const root = new URL("../..", import.meta.url);

async function loadPresentation() {
  const source = await readFile(
    new URL("TaskbarLyrics.App/Web/Lyrics/presentation.js", root),
    "utf8");
  const dom = new JSDOM("", { runScripts: "outside-only" });
  dom.window.eval(source);
  return { api: dom.window.taskbarLyricsPresentation, dom };
}

function createHandlers(calls) {
  return {
    patchTransition: (plan, parameters) => {
      calls.push({ name: "patchTransition", plan, parameters });
      return "patch-result";
    },
    replaceTransition: (plan, parameters) => {
      calls.push({ name: "replaceTransition", plan, parameters });
      return "replace-result";
    },
    rollTransition: (plan, parameters) => {
      calls.push({ name: "rollTransition", plan, parameters });
      return "roll-result";
    },
    layerTransition: (plan, parameters) => {
      calls.push({ name: "layerTransition", plan, parameters });
      return "layer-result";
    }
  };
}

describe("lyrics transition primitives", () => {
  it("maps all seven presentation plans to the four primitives", async () => {
    const { api, dom } = await loadPresentation();

    const mappings = [
      [api.TRANSITIONS.PROGRESS_PATCH, api.TRANSITION_PRIMITIVES.PATCH],
      [api.TRANSITIONS.REPLACE_IN_PLACE, api.TRANSITION_PRIMITIVES.REPLACE],
      [api.TRANSITIONS.IMMEDIATE, api.TRANSITION_PRIMITIVES.REPLACE],
      [api.TRANSITIONS.SINGLE_ROLL, api.TRANSITION_PRIMITIVES.ROLL],
      [api.TRANSITIONS.TRANSLATION_PAIR_ROLL, api.TRANSITION_PRIMITIVES.ROLL],
      [api.TRANSITIONS.SEARCHING_TO_SPECTRUM_ROLL, api.TRANSITION_PRIMITIVES.LAYER],
      [api.TRANSITIONS.LAYER_SWITCH, api.TRANSITION_PRIMITIVES.LAYER]
    ];

    for (const [planKind, primitive] of mappings) {
      expect(api.resolveTransitionPrimitive(planKind)).toBe(primitive);
    }

    dom.window.close();
  });

  it("calls only the mapped handler and preserves plan and parameters", async () => {
    const { api, dom } = await loadPresentation();
    const calls = [];
    const dispatcher = new api.TransitionDispatcher(createHandlers(calls));
    const plans = [
      { kind: api.TRANSITIONS.PROGRESS_PATCH },
      { kind: api.TRANSITIONS.REPLACE_IN_PLACE },
      { kind: api.TRANSITIONS.IMMEDIATE },
      { kind: api.TRANSITIONS.SINGLE_ROLL },
      { kind: api.TRANSITIONS.TRANSLATION_PAIR_ROLL },
      { kind: api.TRANSITIONS.SEARCHING_TO_SPECTRUM_ROLL },
      { kind: api.TRANSITIONS.LAYER_SWITCH }
    ];
    const expectedHandlers = [
      "patchTransition",
      "replaceTransition",
      "replaceTransition",
      "rollTransition",
      "rollTransition",
      "layerTransition",
      "layerTransition"
    ];

    for (let index = 0; index < plans.length; index++) {
      const parameters = { callIndex: index };
      const result = dispatcher.execute(plans[index], parameters);

      expect(result).toBe(`${expectedHandlers[index].replace("Transition", "")}-result`);
      expect(calls[index]).toEqual({
        name: expectedHandlers[index],
        plan: plans[index],
        parameters
      });
      expect(calls[index].plan).toBe(plans[index]);
      expect(calls[index].parameters).toBe(parameters);
    }

    expect(calls).toHaveLength(plans.length);
    expect(calls.map(call => call.name)).toEqual(expectedHandlers);
    dom.window.close();
  });

  it("fails safely for unknown plan kinds and missing handlers", async () => {
    const { api, dom } = await loadPresentation();
    const calls = [];
    const dispatcher = new api.TransitionDispatcher(createHandlers(calls));

    expect(() => api.resolveTransitionPrimitive("futureTransition"))
      .toThrow("Unknown transition plan kind");
    expect(() => dispatcher.execute({ kind: "futureTransition" }, { marker: true }))
      .toThrow("Unknown transition plan kind");
    expect(() => dispatcher.execute(null, { marker: true }))
      .toThrow("Transition plan must be an object");

    for (const handlerName of [
      "patchTransition",
      "replaceTransition",
      "rollTransition",
      "layerTransition"
    ]) {
      const handlers = createHandlers([]);
      delete handlers[handlerName];
      expect(() => new api.TransitionDispatcher(handlers))
        .toThrow("must be a function");
    }

    expect(() => new api.TransitionDispatcher()).toThrow("must be an object");
    expect(() => new api.TransitionDispatcher(null)).toThrow("must be an object");
    dom.window.close();
  });
});
