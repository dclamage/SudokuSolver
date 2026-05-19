/**
 * NFA: every cell in the sequence must be strictly greater than the previous.
 * State tracks the last value seen (0 = no value yet).
 * Accepts when at least one value has been seen (state > 0).
 */

export const type = 'nfa';
export const name = 'Strictly Increasing';
export const numValues = 9;

export const spec = {
    startState: 0,
    transition: (state, value) => {
        if (value > state) return value;
        return undefined; // reject
    },
    accept: (state) => state > 0,
};
