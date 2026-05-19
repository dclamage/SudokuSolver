/**
 * NFA: all consecutive differences between cells must be equal
 * (arithmetic progression).  Accepts at any length >= 1.
 *
 * Mirrors the ISS sandbox example from examples.js.
 */

export const type = 'nfa';
export const name = 'Arithmetic Progression';
export const numValues = 9;

export const spec = {
    startState: { lastVal: null, diff: null },
    transition: (state, value) => {
        if (state.lastVal === null) {
            return { lastVal: value, diff: null };
        }
        const diff = value - state.lastVal;
        if (state.diff === null || state.diff === diff) {
            return { lastVal: value, diff };
        }
        return undefined; // reject — difference changed
    },
    accept: () => true,
};
