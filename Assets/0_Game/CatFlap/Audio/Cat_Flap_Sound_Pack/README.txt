CAT FLAP — SOUND PACK v3  (light)
============================================================

32 cues, 44.1 kHz / 16-bit / stereo WAV.
Palette: G major pentatonic. Soft mallets and kalimba, FM bells,
water drops, cloth and breath textures, thin pads, a short plate
reverb. Nothing in the set has a sharp transient or a buzzer.

v3 takes the weight out. Every cue is lighter and more delicate:
the whole set is high-passed clear of the low end, the pads sit an
octave up, the low thuds are now felt taps, the reverb is shorter
and thinner, and the mallets hand weight from the fundamental to
their upper partials so a note rings like a glass bowl.

Measured against v2: 86% less energy below 300 Hz across the set,
while everything above 6 kHz stays under 0.01% - lighter, not
sharper. Tails are 20% shorter, so cues clear faster when a player
taps quickly, and every file is trimmed to where its sound ends.

Levels are set as a group, not file by file, so the mix is already
balanced: tile taps sit under line completions, which sit under the
win. Loudest cue peaks at -1.5 dBFS; the ambient bed is set apart
at -6 dBFS because it is a bed, not a cue.

Every file is named for where it is used.


-- BOARD -----------------------------------------------

board_note_toggle.wav
    when   Turning pencil / notes mode on or off
    length 0.60 s     peak -13 dBFS     103.4 kB

board_tile_appear.wav
    when   A single tile settling in as the level opens
    length 0.80 s     peak -8.6 dBFS     137.9 kB

board_tile_cat.wav
    when   Placing a cat on an empty tile
    length 0.90 s     peak -1.8 dBFS     155.1 kB

board_tile_clear.wav
    when   Cycling a tile back to empty
    length 0.75 s     peak -9 dBFS     129.2 kB

board_tile_fish.wav
    when   Placing a fish on an empty tile
    length 0.90 s     peak -3.2 dBFS     155.1 kB

board_tile_locked.wav
    when   Tapping a locked clue tile — nothing happens
    length 0.60 s     peak -11.3 dBFS     103.4 kB

board_tiles_intro.wav
    when   The full cascade of tiles as a level opens
    length 1.65 s     peak -11.1 dBFS     284.3 kB

-- LINES -----------------------------------------------

line_complete.wav
    when   A row or column completed correctly
    length 1.35 s     peak -1.6 dBFS     232.6 kB

line_complete_double.wav
    when   A row and a column completed by the same move
    length 1.65 s     peak -1.5 dBFS     284.3 kB

line_progress_half.wav
    when   The board passes halfway
    length 1.65 s     peak -7.5 dBFS     284.3 kB

-- ERRORS ----------------------------------------------

error_line_wrong.wav
    when   A line finished legally but not the way the puzzle solves
    length 1.00 s     peak -6.7 dBFS     172.3 kB

error_move_soft.wav
    when   A move that breaks a rule while the line is still unfinished
    length 0.80 s     peak -7.6 dBFS     137.9 kB

error_rule_break.wav
    when   Three in a row, or a line count that can no longer work
    length 0.75 s     peak -8.4 dBFS     129.2 kB

-- HEARTS ----------------------------------------------

life_empty.wav
    when   The last heart goes — out of lives
    length 2.15 s     peak -8.6 dBFS     370.4 kB

life_heart_lose.wav
    when   A heart is spent
    length 1.35 s     peak -5.9 dBFS     232.6 kB

-- LEVEL -----------------------------------------------

level_coin_reward.wav
    when   Coins or gems landing after a win
    length 1.30 s     peak -8.2 dBFS     224 kB

level_lose.wav
    when   A level ends without a solve
    length 2.00 s     peak -7.3 dBFS     344.6 kB

level_star_tally.wav
    when   Stars counting up on the victory sheet
    length 1.55 s     peak -2.9 dBFS     267.1 kB

level_start.wav
    when   A level opens
    length 1.70 s     peak -10.1 dBFS     292.9 kB

level_win.wav
    when   Level solved — the celebration
    length 2.35 s     peak -1.7 dBFS     404.9 kB

-- INTERFACE -------------------------------------------

ui_button_back.wav
    when   Back, close, cancel
    length 0.70 s     peak -10.5 dBFS     120.6 kB

ui_button_tap.wav
    when   Any primary button
    length 0.70 s     peak -7.2 dBFS     120.6 kB

ui_error_blocked.wav
    when   An action that is not available — no hints left, no hearts left
    length 0.80 s     peak -8.9 dBFS     137.9 kB

ui_hint_reveal.wav
    when   A hint opens
    length 1.65 s     peak -8.7 dBFS     284.3 kB

ui_menu_move.wav
    when   Moving between levels or menu items
    length 0.65 s     peak -11.6 dBFS     112 kB

ui_modal_close.wav
    when   A panel or sheet closes
    length 0.95 s     peak -12.8 dBFS     163.7 kB

ui_modal_open.wav
    when   A panel or sheet slides open
    length 0.95 s     peak -9.2 dBFS     163.7 kB

ui_solve_step.wav
    when   Solve Step places one tile
    length 1.10 s     peak -8.4 dBFS     189.5 kB

ui_toggle_off.wav
    when   A setting switched off
    length 0.80 s     peak -10 dBFS     137.9 kB

ui_toggle_on.wav
    when   A setting switched on
    length 0.80 s     peak -10 dBFS     137.9 kB

ui_undo.wav
    when   Undo
    length 0.85 s     peak -10.2 dBFS     146.5 kB

-- AMBIENCE --------------------------------------------

ambient_menu_loop.wav
    when   Seamless 8-second bed for the menu — loop it
    length 8.00 s     peak -6 dBFS     1378.2 kB


-- NOTES ---------------------------------------------------

ambient_menu_loop.wav loops seamlessly. Every voice completes a
whole number of cycles in 8 seconds and the reverb tail is folded
back onto the head, so the join is inaudible. Set the clip to Loop
and play it about 15 dB under the cues.

board_tile_appear.wav is one tile. Play it per tile with a 40-60 ms
stagger and a rising pitch for the cascade, or use
board_tiles_intro.wav, which is that cascade rendered as one clip.

line_complete_double.wav is for a move that finishes a row and a
column at once. Use it instead of, not on top of, line_complete.

Unity import settings that suit these: Load Type "Decompress On
Load" for everything under 1 s, "Compressed In Memory" for the
longer cues, "Streaming" for the ambience. Vorbis quality 70 keeps
the bell tails clean.

Generated for Mangra Games OU. Free to use and modify in Cat Flap.
