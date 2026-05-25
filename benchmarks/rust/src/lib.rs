pub const SIZE: usize = 9;
pub const BOX_SIZE: usize = 3;
pub const NUM_CELLS: usize = SIZE * SIZE;
pub const ALL_VALUES: u16 = (1 << SIZE) - 1;

const MAX_FRAMES: usize = NUM_CELLS * 2 + 4;
const QUEUE_CAPACITY: usize = 32768;
const MAX_CONSTRAINTS_PER_CELL: usize = 64;

pub const VALUE_MASKS: [u16; SIZE + 1] = build_value_masks();
pub const POPCOUNT: [u8; 1 << SIZE] = build_popcount();
pub const LOW_VALUE: [u8; 1 << SIZE] = build_low_value();
pub const SUM_BY_MASK: [u8; 1 << SIZE] = build_sum_by_mask();
pub const UNITS: [[u8; SIZE]; SIZE * 3] = build_units();
pub const PEER_MATRIX: [u8; NUM_CELLS * NUM_CELLS] = build_peer_matrix();

#[derive(Clone, Copy)]
pub struct SolveOptions {
    pub max_solutions: usize,
    pub trace_limit: usize,
    pub use_house_bilocals: bool,
    pub use_value_conflict_scores: bool,
}

impl Default for SolveOptions {
    fn default() -> Self {
        Self {
            max_solutions: 0,
            trace_limit: 0,
            use_house_bilocals: false,
            use_value_conflict_scores: false,
        }
    }
}

#[derive(Clone, Debug)]
pub struct ParsedPuzzle {
    pub arrows: Vec<Vec<u8>>,
}

#[derive(Clone, Debug)]
pub struct TraceEntry {
    pub depth: usize,
    pub cell: String,
    pub value: u8,
    pub candidates: String,
}

#[derive(Clone, Debug)]
pub struct SolverStats {
    pub guesses: usize,
    pub values_tried: usize,
    pub nodes_searched: usize,
    pub backtracks: usize,
    pub domain_eliminations: usize,
    pub propagation_steps: usize,
    pub trace_hash: String,
    pub trace_hash_value: u32,
    pub trace: Vec<TraceEntry>,
}

#[derive(Clone, Debug)]
struct ArrowConstraint {
    cells: Vec<u8>,
    tuple_masks: Vec<u16>,
}

pub struct ArrowSudokuSolver {
    options: SolveOptions,
    arrows: Vec<ArrowConstraint>,
    constraints_by_cell: [[u8; MAX_CONSTRAINTS_PER_CELL]; NUM_CELLS],
    constraint_counts_by_cell: [u8; NUM_CELLS],
    initial_grid: [u16; NUM_CELLS],
    conflict_scores: [i32; NUM_CELLS],
    value_conflict_scores: [i32; SIZE],
    supports: [u16; SIZE],
    constraint_queued: Vec<u8>,
    constraint_queue: Vec<usize>,
    grid_pool: Vec<[u16; NUM_CELLS]>,
    free_stack: Vec<usize>,
    frame_grid_index: Vec<usize>,
    frame_depth: Vec<usize>,
    frame_pending_cell: Vec<i32>,
    frame_pending_forced: Vec<usize>,
    frame_conflict_value: Vec<u16>,
    trace: Vec<TraceEntry>,
    free_top: usize,
    frame_top: usize,
    queue_head: usize,
    queue_tail: usize,
    propagation_new_singletons: usize,
    branch_cell: i32,
    branch_value_mask: u16,
    branch_count: usize,
    value_score_mask: i32,
    value_score: i32,
    trace_hash: u32,
    solutions: usize,
    guesses: usize,
    values_tried: usize,
    nodes_searched: usize,
    backtracks: usize,
    domain_eliminations: usize,
    propagation_steps: usize,
}

pub fn touch_tables() {
    let _ = VALUE_MASKS[SIZE];
    let _ = POPCOUNT[ALL_VALUES as usize];
    let _ = LOW_VALUE[1];
    let _ = SUM_BY_MASK[ALL_VALUES as usize];
    let _ = UNITS[0][0];
    let _ = PEER_MATRIX[0];
}

pub fn parse_iss_arrow_text(text: &str) -> Result<ParsedPuzzle, String> {
    let source = text.trim();
    let mut search_index = 0usize;
    let mut arrows = Vec::new();

    while search_index < source.len() {
        let Some(relative_arrow_start) = source[search_index..].find(".Arrow~") else {
            break;
        };
        let arrow_start = search_index + relative_arrow_start;
        let payload_start = arrow_start + ".Arrow~".len();
        let payload_end = source[payload_start..]
            .find('.')
            .map(|relative_end| payload_start + relative_end)
            .unwrap_or(source.len());

        let payload = &source[payload_start..payload_end];
        let ids: Vec<&str> = payload.split('~').filter(|id| !id.is_empty()).collect();
        if ids.len() < 2 {
            return Err(format!(
                "Arrow requires a circle and at least one line cell: {payload}"
            ));
        }

        let mut cells = Vec::with_capacity(ids.len());
        for id in ids {
            cells.push(parse_cell_id(id)?);
        }
        arrows.push(cells);
        search_index = payload_end;
    }

    if arrows.is_empty() {
        return Err("No .Arrow constraints found in ISS text.".to_string());
    }

    Ok(ParsedPuzzle { arrows })
}

impl ArrowSudokuSolver {
    pub fn new(puzzle: &ParsedPuzzle, options: SolveOptions) -> Result<Self, String> {
        let mut arrows = Vec::with_capacity(puzzle.arrows.len());
        for cells in &puzzle.arrows {
            arrows.push(build_arrow(cells)?);
        }

        let (constraints_by_cell, constraint_counts_by_cell) = build_constraints_by_cell(&arrows)?;
        let mut solver = Self {
            options,
            arrows,
            constraints_by_cell,
            constraint_counts_by_cell,
            initial_grid: [ALL_VALUES; NUM_CELLS],
            conflict_scores: [0; NUM_CELLS],
            value_conflict_scores: [0; SIZE],
            supports: [0; SIZE],
            constraint_queued: Vec::new(),
            constraint_queue: vec![0; QUEUE_CAPACITY],
            grid_pool: vec![[0; NUM_CELLS]; MAX_FRAMES],
            free_stack: vec![0; MAX_FRAMES],
            frame_grid_index: vec![0; MAX_FRAMES],
            frame_depth: vec![0; MAX_FRAMES],
            frame_pending_cell: vec![-1; MAX_FRAMES],
            frame_pending_forced: vec![0; MAX_FRAMES],
            frame_conflict_value: vec![0; MAX_FRAMES],
            trace: Vec::with_capacity(options.trace_limit),
            free_top: 0,
            frame_top: 0,
            queue_head: 0,
            queue_tail: 0,
            propagation_new_singletons: 0,
            branch_cell: -1,
            branch_value_mask: 0,
            branch_count: 0,
            value_score_mask: 0,
            value_score: 0,
            trace_hash: 0x811c9dc5,
            solutions: 0,
            guesses: 0,
            values_tried: 0,
            nodes_searched: 0,
            backtracks: 0,
            domain_eliminations: 0,
            propagation_steps: 0,
        };

        solver
            .constraint_queued
            .resize(UNITS.len() + solver.arrows.len(), 0);
        solver.seed_conflict_scores();
        Ok(solver)
    }

    pub fn count_solutions(&mut self, max_solutions: usize) -> Result<usize, String> {
        self.reset_search_state();

        let root_index = self.alloc_grid()?;
        self.grid_pool[root_index].copy_from_slice(&self.initial_grid);
        self.propagation_new_singletons = 0;
        if !self.propagate_all(root_index)? {
            self.backtracks += 1;
            self.free_grid(root_index);
            return Ok(0);
        }

        self.values_tried += self.propagation_new_singletons;
        self.push_frame(root_index, 0, -1, 0, 0)?;
        self.search(max_solutions)?;
        Ok(if max_solutions > 0 && self.solutions > max_solutions {
            max_solutions
        } else {
            self.solutions
        })
    }

    pub fn result_stats(&self) -> SolverStats {
        SolverStats {
            guesses: self.guesses,
            values_tried: self.values_tried,
            nodes_searched: self.nodes_searched,
            backtracks: self.backtracks,
            domain_eliminations: self.domain_eliminations,
            propagation_steps: self.propagation_steps,
            trace_hash: format!("{:08x}", self.trace_hash),
            trace_hash_value: self.trace_hash,
            trace: self.trace.clone(),
        }
    }

    fn seed_conflict_scores(&mut self) {
        self.conflict_scores.fill((SIZE * 3) as i32);
        for arrow in &self.arrows {
            let priority = (SIZE * 2).saturating_sub(arrow.cells.len()).max(SIZE) as i32;
            for &cell in &arrow.cells {
                self.conflict_scores[cell as usize] += priority;
            }
        }
    }

    fn search(&mut self, max_solutions: usize) -> Result<(), String> {
        while self.frame_top > 0 {
            self.frame_top -= 1;
            let frame_index = self.frame_top;
            let grid_index = self.frame_grid_index[frame_index];
            let depth = self.frame_depth[frame_index];
            let pending_cell = self.frame_pending_cell[frame_index];
            let conflict_value = self.frame_conflict_value[frame_index];

            if pending_cell >= 0 {
                self.propagation_new_singletons = self.frame_pending_forced[frame_index];
                if !self.propagate_from_cell(grid_index, pending_cell as usize)? {
                    self.backtracks += 1;
                    self.increment_conflict(pending_cell as usize, conflict_value);
                    self.free_grid(grid_index);
                    continue;
                }
                self.values_tried += self.propagation_new_singletons;
            }

            self.nodes_searched += 1;

            self.select_best_branch(grid_index);
            if self.branch_cell < 0 {
                self.solutions += 1;
                self.mix_trace(255, depth, self.solutions & 255);
                self.free_grid(grid_index);
                if max_solutions > 0 && self.solutions >= max_solutions {
                    break;
                }
                continue;
            }

            let cell = self.branch_cell as usize;
            let mask = self.grid_pool[grid_index][cell];
            let value_mask = self.branch_value_mask;
            self.guesses += 1;
            self.values_tried += 1;
            self.mix_trace(cell, depth, LOW_VALUE[value_mask as usize] as usize);

            if self.trace.len() < self.options.trace_limit {
                self.trace.push(TraceEntry {
                    depth,
                    cell: cell_to_id(cell),
                    value: LOW_VALUE[value_mask as usize],
                    candidates: mask_to_digits(mask),
                });
            }

            let clear_index = self.alloc_grid()?;
            let source_grid = self.grid_pool[grid_index];
            self.grid_pool[clear_index] = source_grid;
            let clear_mask = mask & !value_mask;
            self.grid_pool[clear_index][cell] = clear_mask;
            self.push_frame(
                clear_index,
                depth + 1,
                cell as i32,
                usize::from((clear_mask & (clear_mask - 1)) == 0),
                value_mask,
            )?;

            self.grid_pool[grid_index][cell] = value_mask;
            self.push_frame(grid_index, depth + 1, cell as i32, 0, value_mask)?;
        }

        while self.frame_top > 0 {
            self.frame_top -= 1;
            self.free_grid(self.frame_grid_index[self.frame_top]);
        }

        Ok(())
    }

    fn reset_search_state(&mut self) {
        self.free_top = MAX_FRAMES;
        for i in 0..MAX_FRAMES {
            self.free_stack[i] = i;
        }
        self.frame_top = 0;
    }

    fn alloc_grid(&mut self) -> Result<usize, String> {
        if self.free_top == 0 {
            return Err("Search grid pool exhausted.".to_string());
        }
        self.free_top -= 1;
        Ok(self.free_stack[self.free_top])
    }

    fn free_grid(&mut self, grid_index: usize) {
        self.free_stack[self.free_top] = grid_index;
        self.free_top += 1;
    }

    fn push_frame(
        &mut self,
        grid_index: usize,
        depth: usize,
        pending_cell: i32,
        pending_forced: usize,
        conflict_value: u16,
    ) -> Result<(), String> {
        if self.frame_top == MAX_FRAMES {
            return Err("Search frame stack exhausted.".to_string());
        }

        let frame_index = self.frame_top;
        self.frame_top += 1;
        self.frame_grid_index[frame_index] = grid_index;
        self.frame_depth[frame_index] = depth;
        self.frame_pending_cell[frame_index] = pending_cell;
        self.frame_pending_forced[frame_index] = pending_forced;
        self.frame_conflict_value[frame_index] = conflict_value;
        Ok(())
    }

    fn propagate_all(&mut self, grid_index: usize) -> Result<bool, String> {
        self.reset_queue();
        for constraint_index in 0..self.constraint_queued.len() {
            self.enqueue_constraint(constraint_index)?;
        }
        self.drain_queue(grid_index)
    }

    fn propagate_from_cell(&mut self, grid_index: usize, cell: usize) -> Result<bool, String> {
        self.reset_queue();
        self.enqueue_cell(cell)?;
        self.drain_queue(grid_index)
    }

    fn reset_queue(&mut self) {
        self.constraint_queued.fill(0);
        self.queue_head = 0;
        self.queue_tail = 0;
    }

    fn enqueue_cell(&mut self, cell: usize) -> Result<(), String> {
        let count = self.constraint_counts_by_cell[cell] as usize;
        for i in 0..count {
            self.enqueue_constraint(self.constraints_by_cell[cell][i] as usize)?;
        }
        Ok(())
    }

    fn enqueue_constraint(&mut self, constraint_index: usize) -> Result<(), String> {
        if self.constraint_queued[constraint_index] != 0 {
            return Ok(());
        }
        self.constraint_queued[constraint_index] = 1;
        if self.queue_tail == self.constraint_queue.len() {
            return Err("Constraint queue exhausted.".to_string());
        }
        self.constraint_queue[self.queue_tail] = constraint_index;
        self.queue_tail += 1;
        Ok(())
    }

    fn drain_queue(&mut self, grid_index: usize) -> Result<bool, String> {
        while self.queue_head < self.queue_tail {
            let constraint_index = self.constraint_queue[self.queue_head];
            self.queue_head += 1;
            self.constraint_queued[constraint_index] = 0;
            self.propagation_steps += 1;

            let valid = if constraint_index < UNITS.len() {
                self.enforce_house(grid_index, constraint_index)?
            } else {
                self.enforce_arrow(grid_index, constraint_index - UNITS.len())?
            };

            if !valid {
                return Ok(false);
            }
        }

        Ok(true)
    }

    fn enforce_house(&mut self, grid_index: usize, unit_index: usize) -> Result<bool, String> {
        let unit = UNITS[unit_index];
        let mut fixed_mask = 0u16;

        for cell in unit {
            let mask = self.grid_pool[grid_index][cell as usize];
            if mask == 0 {
                return Ok(false);
            }
            if mask & (mask - 1) == 0 {
                if fixed_mask & mask != 0 {
                    return Ok(false);
                }
                fixed_mask |= mask;
            }
        }

        let mut once = 0u16;
        let mut twice = 0u16;
        for cell in unit {
            let cell = cell as usize;
            let old_mask = self.grid_pool[grid_index][cell];
            let mut next_mask = old_mask;

            if old_mask & (old_mask - 1) != 0 {
                next_mask = old_mask & !fixed_mask;
                if next_mask == 0 {
                    return Ok(false);
                }
                if next_mask != old_mask {
                    self.narrow_cell(grid_index, cell, next_mask);
                    self.enqueue_cell(cell)?;
                }
            }

            twice |= once & next_mask;
            once |= next_mask;
        }

        if once & ALL_VALUES != ALL_VALUES {
            return Ok(false);
        }

        let mut hidden_singles = once & !twice;
        while hidden_singles != 0 {
            let bit = hidden_singles & hidden_singles.wrapping_neg();
            hidden_singles ^= bit;
            let mut last_cell = -1i32;

            for cell in unit {
                if self.grid_pool[grid_index][cell as usize] & bit != 0 {
                    last_cell = cell as i32;
                    break;
                }
            }

            if last_cell < 0 {
                return Ok(false);
            }
            let last_cell = last_cell as usize;
            if self.grid_pool[grid_index][last_cell] != bit {
                self.narrow_cell(grid_index, last_cell, bit);
                self.enqueue_cell(last_cell)?;
            }
        }

        Ok(true)
    }

    fn enforce_arrow(&mut self, grid_index: usize, arrow_index: usize) -> Result<bool, String> {
        let width = self.arrows[arrow_index].cells.len();
        self.supports[..width].fill(0);

        {
            let arrow = &self.arrows[arrow_index];
            let tuple_masks = &arrow.tuple_masks;
            let cells = &arrow.cells;
            let mut offset = 0usize;
            while offset < tuple_masks.len() {
                let mut valid = true;
                for i in 0..width {
                    if self.grid_pool[grid_index][cells[i] as usize] & tuple_masks[offset + i] == 0
                    {
                        valid = false;
                        break;
                    }
                }

                if valid {
                    for i in 0..width {
                        self.supports[i] |= tuple_masks[offset + i];
                    }
                }
                offset += width;
            }
        }

        for i in 0..width {
            let cell = self.arrows[arrow_index].cells[i] as usize;
            let old_mask = self.grid_pool[grid_index][cell];
            let next_mask = old_mask & self.supports[i];
            if next_mask == 0 {
                return Ok(false);
            }
            if next_mask != old_mask {
                self.narrow_cell(grid_index, cell, next_mask);
                self.enqueue_cell(cell)?;
            }
        }

        Ok(true)
    }

    fn narrow_cell(&mut self, grid_index: usize, cell: usize, next_mask: u16) {
        let old_mask = self.grid_pool[grid_index][cell];
        self.domain_eliminations +=
            (POPCOUNT[old_mask as usize] - POPCOUNT[next_mask as usize]) as usize;
        if old_mask & (old_mask - 1) != 0 && next_mask & (next_mask - 1) == 0 {
            self.propagation_new_singletons += 1;
        }
        self.grid_pool[grid_index][cell] = next_mask;
    }

    fn select_best_branch(&mut self, grid_index: usize) {
        self.set_max_value_score();
        let mut best_cell = -1i32;
        let mut best_score = -1.0f64;
        let mut best_count = 1usize;
        let mut best_mask = 0u16;

        for cell in 0..NUM_CELLS {
            let mask = self.grid_pool[grid_index][cell];
            let count = POPCOUNT[mask as usize] as usize;
            if count <= 1 {
                continue;
            }

            let mut score_unnormalized = self.conflict_scores[cell] as f64;
            if mask & self.value_score_mask as u16 != 0 {
                score_unnormalized += self.value_score as f64 * 0.2;
            }
            let score = score_unnormalized / count as f64;
            if best_cell < 0 || score > best_score || (score == best_score && count < best_count) {
                best_cell = cell as i32;
                best_score = score;
                best_count = count;
                best_mask = mask;
            }
        }

        if best_cell < 0 {
            self.branch_cell = -1;
            self.branch_value_mask = 0;
            self.branch_count = 0;
            return;
        }

        if self.options.use_house_bilocals && best_count > 2 && best_score > 0.0 {
            self.find_best_house_bilocal(grid_index, best_score);
            if self.branch_cell >= 0 {
                return;
            }
        }

        self.branch_cell = best_cell;
        self.branch_value_mask = best_mask & best_mask.wrapping_neg();
        self.branch_count = best_count;
    }

    fn find_best_house_bilocal(&mut self, grid_index: usize, current_score: f64) {
        let mut best_cell = -1i32;
        let mut best_value_mask = 0u16;
        let mut best_score = current_score;

        for unit in UNITS {
            let mut once = 0u16;
            let mut twice = 0u16;
            let mut more = 0u16;

            for cell in unit {
                let mask = self.grid_pool[grid_index][cell as usize];
                more |= twice & mask;
                twice |= once & mask;
                once |= mask;
            }

            let mut exactly_twice = twice & !more;
            while exactly_twice != 0 {
                let value_mask = exactly_twice & exactly_twice.wrapping_neg();
                exactly_twice ^= value_mask;

                let mut cell0 = -1i32;
                let mut cell1 = -1i32;
                let mut max_score = 0i32;
                for cell in unit {
                    let cell = cell as usize;
                    if self.grid_pool[grid_index][cell] & value_mask == 0 {
                        continue;
                    }
                    if self.grid_pool[grid_index][cell] & (self.grid_pool[grid_index][cell] - 1)
                        == 0
                    {
                        cell0 = -1;
                        cell1 = -1;
                        break;
                    }
                    if cell0 < 0 {
                        cell0 = cell as i32;
                    } else {
                        cell1 = cell as i32;
                    }
                    if self.conflict_scores[cell] > max_score {
                        max_score = self.conflict_scores[cell];
                    }
                }

                if cell0 < 0 || cell1 < 0 {
                    continue;
                }

                let score = max_score as f64 * 0.5;
                if score <= best_score {
                    continue;
                }

                best_score = score;
                best_value_mask = value_mask;
                best_cell = if self.conflict_scores[cell1 as usize]
                    >= self.conflict_scores[cell0 as usize]
                {
                    cell1
                } else {
                    cell0
                };
            }
        }

        self.branch_cell = best_cell;
        self.branch_value_mask = best_value_mask;
        self.branch_count = if best_cell >= 0 { 2 } else { 0 };
    }

    fn set_max_value_score(&mut self) {
        if !self.options.use_value_conflict_scores {
            self.value_score_mask = 0;
            self.value_score = 0;
            return;
        }

        let mut max_score = 0i32;
        let mut min_score = i32::MAX;
        let mut value_mask = 0i32;

        for i in 0..SIZE {
            let score = self.value_conflict_scores[i];
            if score > max_score {
                max_score = score;
                value_mask = 1 << i;
            }
            if score != 0 && score < min_score {
                min_score = score;
            }
        }

        if max_score < SIZE as i32 || (max_score << 1) <= min_score * 3 {
            self.value_score_mask = 0;
            self.value_score = 0;
            return;
        }

        self.value_score_mask = value_mask;
        self.value_score = max_score;
    }

    fn increment_conflict(&mut self, cell: usize, value_mask: u16) {
        self.conflict_scores[cell] += 1;
        if value_mask != 0 {
            self.value_conflict_scores[(LOW_VALUE[value_mask as usize] - 1) as usize] += 1;
        }
    }

    fn mix_trace(&mut self, cell: usize, depth: usize, value: usize) {
        self.trace_hash = (self.trace_hash ^ (cell as u32 + 1)).wrapping_mul(16777619);
        self.trace_hash = (self.trace_hash ^ (depth as u32 + 1)).wrapping_mul(16777619);
        self.trace_hash = (self.trace_hash ^ value as u32).wrapping_mul(16777619);
    }
}

fn build_arrow(cells: &[u8]) -> Result<ArrowConstraint, String> {
    let mut tuples = Vec::new();
    let mut values = vec![0u8; cells.len()];

    for circle_value in 1..=SIZE as u8 {
        values[0] = circle_value;
        build_arrow_tuples(cells, &mut values, &mut tuples, 1, circle_value as i32);
    }

    let tuple_masks = tuples
        .iter()
        .map(|&value| VALUE_MASKS[value as usize])
        .collect();

    Ok(ArrowConstraint {
        cells: cells.to_vec(),
        tuple_masks,
    })
}

fn build_constraints_by_cell(
    arrows: &[ArrowConstraint],
) -> Result<([[u8; MAX_CONSTRAINTS_PER_CELL]; NUM_CELLS], [u8; NUM_CELLS]), String> {
    let mut constraints_by_cell = [[0u8; MAX_CONSTRAINTS_PER_CELL]; NUM_CELLS];
    let mut counts = [0u8; NUM_CELLS];

    for unit_index in 0..UNITS.len() {
        for cell in UNITS[unit_index] {
            push_cell_constraint(
                &mut constraints_by_cell,
                &mut counts,
                cell as usize,
                unit_index,
            )?;
        }
    }

    for (arrow_index, arrow) in arrows.iter().enumerate() {
        let constraint_index = UNITS.len() + arrow_index;
        for &cell in &arrow.cells {
            push_cell_constraint(
                &mut constraints_by_cell,
                &mut counts,
                cell as usize,
                constraint_index,
            )?;
        }
    }

    Ok((constraints_by_cell, counts))
}

fn push_cell_constraint(
    constraints_by_cell: &mut [[u8; MAX_CONSTRAINTS_PER_CELL]; NUM_CELLS],
    counts: &mut [u8; NUM_CELLS],
    cell: usize,
    constraint_index: usize,
) -> Result<(), String> {
    if constraint_index > u8::MAX as usize {
        return Err("Too many constraints for byte-sized constraint indexes.".to_string());
    }
    let count = counts[cell] as usize;
    if count == MAX_CONSTRAINTS_PER_CELL {
        return Err(format!("Too many constraints attached to cell {cell}."));
    }
    constraints_by_cell[cell][count] = constraint_index as u8;
    counts[cell] += 1;
    Ok(())
}

fn build_arrow_tuples(
    cells: &[u8],
    values: &mut [u8],
    tuples: &mut Vec<u8>,
    index: usize,
    remaining_sum: i32,
) {
    if index == cells.len() {
        if remaining_sum == 0 && tuple_respects_peers(cells, values) {
            tuples.extend_from_slice(values);
        }
        return;
    }

    let remaining_cells = cells.len() - index - 1;
    let min_remaining = remaining_cells as i32;
    let max_remaining = (remaining_cells * SIZE) as i32;
    let max_value = (SIZE as i32).min(remaining_sum - min_remaining);

    for value in 1..=max_value {
        let next_remaining = remaining_sum - value;
        if next_remaining < min_remaining || next_remaining > max_remaining {
            continue;
        }
        values[index] = value as u8;
        build_arrow_tuples(cells, values, tuples, index + 1, next_remaining);
    }
}

fn tuple_respects_peers(cells: &[u8], values: &[u8]) -> bool {
    for i in 0..cells.len() - 1 {
        for j in i + 1..cells.len() {
            if values[i] == values[j]
                && PEER_MATRIX[cells[i] as usize * NUM_CELLS + cells[j] as usize] != 0
            {
                return false;
            }
        }
    }
    true
}

fn parse_cell_id(id: &str) -> Result<u8, String> {
    let bytes = id.trim().as_bytes();
    if bytes.len() != 4 || !matches!(bytes[0], b'R' | b'r') || !matches!(bytes[2], b'C' | b'c') {
        return Err(format!("Unsupported ISS cell id: {id}"));
    }

    let row = bytes[1].wrapping_sub(b'1') as usize;
    let col = bytes[3].wrapping_sub(b'1') as usize;
    if row >= SIZE || col >= SIZE {
        return Err(format!("Unsupported ISS cell id: {id}"));
    }

    Ok((row * SIZE + col) as u8)
}

fn cell_to_id(cell: usize) -> String {
    format!("R{}C{}", cell / SIZE + 1, cell % SIZE + 1)
}

fn mask_to_digits(mask: u16) -> String {
    let mut text = String::new();
    for value in 1..=SIZE {
        if mask & VALUE_MASKS[value] != 0 {
            text.push(char::from(b'0' + value as u8));
        }
    }
    text
}

const fn build_value_masks() -> [u16; SIZE + 1] {
    let mut masks = [0u16; SIZE + 1];
    let mut value = 1usize;
    while value <= SIZE {
        masks[value] = 1u16 << (value - 1);
        value += 1;
    }
    masks
}

const fn build_popcount() -> [u8; 1 << SIZE] {
    let mut result = [0u8; 1 << SIZE];
    let mut mask = 1usize;
    while mask <= ALL_VALUES as usize {
        result[mask] = result[mask & (mask - 1)] + 1;
        mask += 1;
    }
    result
}

const fn build_low_value() -> [u8; 1 << SIZE] {
    let mut result = [0u8; 1 << SIZE];
    let mut mask = 1usize;
    while mask <= ALL_VALUES as usize {
        let mut value = 1usize;
        while value <= SIZE {
            if mask & (1usize << (value - 1)) != 0 {
                result[mask] = value as u8;
                break;
            }
            value += 1;
        }
        mask += 1;
    }
    result
}

const fn build_sum_by_mask() -> [u8; 1 << SIZE] {
    let mut result = [0u8; 1 << SIZE];
    let mut mask = 1usize;
    while mask <= ALL_VALUES as usize {
        result[mask] = result[mask & (mask - 1)] + LOW_VALUE[mask] as u8;
        mask += 1;
    }
    result
}

const fn build_units() -> [[u8; SIZE]; SIZE * 3] {
    let mut units = [[0u8; SIZE]; SIZE * 3];
    let mut unit_index = 0usize;

    let mut row = 0usize;
    while row < SIZE {
        let mut col = 0usize;
        while col < SIZE {
            units[unit_index][col] = (row * SIZE + col) as u8;
            col += 1;
        }
        unit_index += 1;
        row += 1;
    }

    let mut col = 0usize;
    while col < SIZE {
        let mut row = 0usize;
        while row < SIZE {
            units[unit_index][row] = (row * SIZE + col) as u8;
            row += 1;
        }
        unit_index += 1;
        col += 1;
    }

    let mut box_row = 0usize;
    while box_row < BOX_SIZE {
        let mut box_col = 0usize;
        while box_col < BOX_SIZE {
            let mut index = 0usize;
            let mut dr = 0usize;
            while dr < BOX_SIZE {
                let mut dc = 0usize;
                while dc < BOX_SIZE {
                    units[unit_index][index] =
                        ((box_row * BOX_SIZE + dr) * SIZE + box_col * BOX_SIZE + dc) as u8;
                    index += 1;
                    dc += 1;
                }
                dr += 1;
            }
            unit_index += 1;
            box_col += 1;
        }
        box_row += 1;
    }

    units
}

const fn build_peer_matrix() -> [u8; NUM_CELLS * NUM_CELLS] {
    let mut matrix = [0u8; NUM_CELLS * NUM_CELLS];
    let mut cell = 0usize;
    while cell < NUM_CELLS {
        let row = cell / SIZE;
        let col = cell % SIZE;
        let box_row = row / BOX_SIZE;
        let box_col = col / BOX_SIZE;

        let mut other = 0usize;
        while other < NUM_CELLS {
            if cell != other {
                let other_row = other / SIZE;
                let other_col = other % SIZE;
                if row == other_row
                    || col == other_col
                    || (box_row == other_row / BOX_SIZE && box_col == other_col / BOX_SIZE)
                {
                    matrix[cell * NUM_CELLS + other] = 1;
                }
            }
            other += 1;
        }
        cell += 1;
    }
    matrix
}

#[cfg(target_arch = "wasm32")]
mod wasm_exports {
    use super::*;

    struct WasmState {
        solver: ArrowSudokuSolver,
        max_solutions: usize,
        solutions: usize,
    }

    static mut STATE: *mut WasmState = std::ptr::null_mut();

    #[no_mangle]
    pub extern "C" fn bench_alloc(len: usize) -> *mut u8 {
        let mut buffer = Vec::<u8>::with_capacity(len);
        let ptr = buffer.as_mut_ptr();
        std::mem::forget(buffer);
        ptr
    }

    #[no_mangle]
    pub unsafe extern "C" fn bench_dealloc(ptr: *mut u8, len: usize) {
        if !ptr.is_null() && len > 0 {
            drop(Vec::from_raw_parts(ptr, 0, len));
        }
    }

    #[no_mangle]
    pub unsafe extern "C" fn bench_setup(
        ptr: *const u8,
        len: usize,
        max_solutions: usize,
        trace_limit: usize,
    ) -> i32 {
        let bytes = std::slice::from_raw_parts(ptr, len);
        let Ok(text) = std::str::from_utf8(bytes) else {
            return -1;
        };
        let Ok(puzzle) = parse_iss_arrow_text(text) else {
            return -2;
        };
        let options = SolveOptions {
            max_solutions,
            trace_limit,
            ..SolveOptions::default()
        };
        let Ok(solver) = ArrowSudokuSolver::new(&puzzle, options) else {
            return -3;
        };
        if !STATE.is_null() {
            drop(Box::from_raw(STATE));
        }
        STATE = Box::into_raw(Box::new(WasmState {
            solver,
            max_solutions,
            solutions: 0,
        }));
        0
    }

    #[no_mangle]
    pub unsafe extern "C" fn bench_free_state() {
        if !STATE.is_null() {
            drop(Box::from_raw(STATE));
            STATE = std::ptr::null_mut();
        }
    }

    #[no_mangle]
    pub unsafe extern "C" fn bench_solve() -> i32 {
        if STATE.is_null() {
            return -1;
        }
        let state = &mut *STATE;
        match state.solver.count_solutions(state.max_solutions) {
            Ok(solutions) => {
                state.solutions = solutions;
                0
            }
            Err(_) => -2,
        }
    }

    #[no_mangle]
    pub unsafe extern "C" fn bench_get_solutions() -> usize {
        if STATE.is_null() {
            0
        } else {
            (*STATE).solutions
        }
    }

    #[no_mangle]
    pub unsafe extern "C" fn bench_get_guesses() -> usize {
        if STATE.is_null() {
            0
        } else {
            (*STATE).solver.guesses
        }
    }

    #[no_mangle]
    pub unsafe extern "C" fn bench_get_values_tried() -> usize {
        if STATE.is_null() {
            0
        } else {
            (*STATE).solver.values_tried
        }
    }

    #[no_mangle]
    pub unsafe extern "C" fn bench_get_nodes_searched() -> usize {
        if STATE.is_null() {
            0
        } else {
            (*STATE).solver.nodes_searched
        }
    }

    #[no_mangle]
    pub unsafe extern "C" fn bench_get_backtracks() -> usize {
        if STATE.is_null() {
            0
        } else {
            (*STATE).solver.backtracks
        }
    }

    #[no_mangle]
    pub unsafe extern "C" fn bench_get_domain_eliminations() -> usize {
        if STATE.is_null() {
            0
        } else {
            (*STATE).solver.domain_eliminations
        }
    }

    #[no_mangle]
    pub unsafe extern "C" fn bench_get_propagation_steps() -> usize {
        if STATE.is_null() {
            0
        } else {
            (*STATE).solver.propagation_steps
        }
    }

    #[no_mangle]
    pub unsafe extern "C" fn bench_get_trace_hash() -> u32 {
        if STATE.is_null() {
            0
        } else {
            (*STATE).solver.trace_hash
        }
    }
}
