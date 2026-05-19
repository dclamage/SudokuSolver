/**
 * Binary lookup: the two cells must sum to exactly 10.
 * Valid pairs: (1,9) (2,8) (3,7) (4,6) (5,5) (6,4) (7,3) (8,2) (9,1).
 */

export const type = 'binary';
export const name = 'Sum to 10';
export const numValues = 9;

export function isAllowed(v1, v2) {
    return v1 + v2 === 10;
}
