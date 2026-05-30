/**
 * Short literal / ordering hints appended in parentheses to keyword summaries.
 * Keys must match MTGJSON keyword strings exactly (same casing as Keywords.json).
 */

/**
 * @param {string} k keyword name
 * @param {string} summary full summary string before appending
 */
export function inferPlainHint(k, summary) {
  if (/walk$/i.test(k)) {
    if (k === "Landwalk")
      return "the card names one land subtype; check the defending player's lands for that subtype on the type line";
    if (k === "Nonbasic landwalk")
      return "if they control any nonbasic land, one creature cannot wall this alone";
    if (k === "Legendary landwalk")
      return "if they control any legendary land, one creature cannot wall this alone";
    const subtype = k.replace(/walk$/i, "");
    return `if the defending player controls a ${subtype} land, one creature usually cannot block this alone`;
  }
  if (/cycling$/i.test(k))
    return "you are not casting this card—you pay, discard it from hand, then do what the line says";

  if (summary.includes("keyword ability — read reminder"))
    return "printed reminder text on the card is the step-by-step script if this summary feels abstract";
  if (summary.includes("keyword action —"))
    return "a defined rules action; the spell or ability that uses it tells you exactly when it happens";
  if (summary.includes("Italicized ability word"))
    return "italics name a theme—the regular text next to it on the card says exactly when it counts";

  return null;
}

/** @type {Record<string, string>} */
export const PLAIN_HINTS = {
  Absorb: "some incoming damage becomes +1/+1 counters instead—N is on the card",
  Affinity: "count permanents of the listed type on your side; each one makes the spell cheaper",
  Afflict: "blocking still hurts the defending player—life loss is separate from combat damage",
  Afterlife: "when it dies for any reason, Spirit tokens show up afterward",
  Annihilator: "attack triggers forced sacrifices—how many is printed as N",
  Ascend: "count your permanents; at 10+ you turn on a game-long flag",
  Assist: "a teammate may chip in generic mana while you cast",
  Backup: "enters; you may buff another creature; if you do, that creature copies listed abilities until end of turn",
  Bargain: "optional sacrifice of an artifact, enchantment, or token during casting for a bonus paragraph",
  Blitz: "rush mode: haste, short life, draw if it dies—timing printed on the card",
  Bloodthirst: "only works if an opponent already took damage this turn before this resolves",
  Boast: "after this attacks once this turn, you may pay once for the boast effect",
  Cascade: "free follow-up spell from the top of library—stops at first cheaper nonland",
  Casualty: "optional: sacrifice a big enough creature as you cast to copy the spell",
  Champion: "enters: exile a friend or die; friend returns when this leaves",
  Changeling: "counts as every creature type at once for any rule that cares about types",
  Cipher: "damage can attach this to a creature; later combat damage may recast a copy",
  Convoke: "tap your creatures to help pay—each creature pays {1} or one mana of its colors",
  Crew: "tap your creatures with enough total power; Vehicle becomes a creature until end of turn",
  Cycling: "pay, discard from hand, then draw or fetch—exact effect is printed on the line",
  Dash: "alternate cost: haste this turn, then return to hand at end of turn",
  Deathtouch: "any positive damage it deals to a creature is treated as lethal",
  Decayed: "cannot block; if it attacks, it is sacrificed at end of combat—one-shot attacker",
  Defender: "cannot attack unless another effect explicitly allows attacking",
  Delve: "while casting, you may exile graveyard cards to pay for generic mana",
  Demonstrate: "optional copy for you; if you take it, an opponent gets a copy too",
  Devoid: "colorless for rules even when mana symbols look colored",
  Disturb: "may cast from graveyard as back face for disturb cost—follow exile rules on card",
  "Double strike": "damage happens in the first-strike window and again in the normal combat window",
  Dredge: "sometimes replace a draw: mill N, return this from graveyard to hand",
  Echo: "next upkeep, pay echo or sacrifice—recurring cost",
  Embalm: "from graveyard, exile to make a token copy—colors and types change as printed",
  Equip: "attach Equipment to your creature; usually sorcery-speed unless the card says not",
  Evoke: "cheaper on purpose: it enters, then you sacrifice it immediately",
  Exalted: "only fires when exactly one of your creatures attacks alone",
  Exploit: "on enter you may sacrifice a creature; if you do, the exploit sentence runs",
  Extort: "each spell you cast may tax WB to drain each opponent a little and gain life",
  Fabricate: "on enter you choose counters or Servo tokens—N is printed",
  "First strike": "in combat, it assigns damage in the earlier combat step unless the other creature also has first strike or double strike",
  Flash: "you may cast it whenever you could cast an instant",
  Flashback: "one-time cast from graveyard; exile on resolution",
  Flying: "only creatures with flying or reach can block it",
  Foretell: "on your turn you may pay {2} to stash it face down; later cast for foretell cost",
  Goad: "that creature must attack if it can and should attack someone other than you",
  Graft: "enters with counters; when a creature enters you may move one counter to it",
  Haste: "can attack and use tap abilities the same turn it enters—no wait step",
  Hexproof: "opponents cannot choose it as a target; you still can unless an effect forbids",
  "Hexproof from": "opponents cannot target it with sources that match the named quality",
  Indestructible: "destroy and lethal damage do not kill it; exile, bounce, and sacrifice still work",
  Infect: "damage to creatures shrinks them with counters; damage to players is poison",
  Lifelink: "damage it deals also heals its controller by the same number",
  Menace: "needs two or more blockers together—one solo blocker cannot pick this fight",
  Mentor: "when it attacks, you may grow a smaller attacker",
  Mill: "move cards from the top of a library to the graveyard—count matters",
  Morph: "may enter as a face-down 2/2; later pay morph cost to flip—special action, not a spell on the stack",
  Mutate: "cast onto your non-Human; stacks merge—read whether new card is on top or bottom",
  Ninjutsu: "during combat after no blockers: return an unblocked attacker to hand to put this in tapped and attacking",
  Partner: "commander: two commanders if both say partner",
  Persist: "dies without a -1/-1 counter: return with one; otherwise stays dead",
  Poisonous: "combat damage also gives poison counters—separate track from life",
  Protection: "sources with that quality cannot damage, enchant, block, or target it",
  Prowess: "each noncreature spell you cast gives +1/+1 until end of turn",
  Reach: "can block flying creatures without having flying itself",
  Scry: "peek at the top; you choose what goes back on top vs bottom",
  Shroud: "no player may target it—not you, not opponents",
  Skulk: "cannot be blocked by anything with higher power than this creature",
  Surveil: "peek at the top; some cards may go to graveyard instead of back on top",
  Suspend: "exile with counters; remove one each upkeep; cast free when last counter leaves",
  Toxic: "combat damage to players also adds poison counters equal to toxic number",
  Trample: "after assigning lethal to all blockers, extra damage can hit the player or planeswalker",
  Undying: "dies with no +1/+1 counter: return with one; otherwise stays dead",
  Vigilance: "attacking does not tap it—it can still block afterward",
  Ward: "when an opponent targets it, they must pay or the spell or ability fails",
  Fight: "the two creatures each deal damage equal to their power to the other—no combat step",
  Counter: "removes a spell or ability from the stack so it does not resolve",
  Destroy: "moves a permanent from battlefield to graveyard—different from damage unless stated",
  Exile: "moves a card to the exile zone—out of play until something brings it back",
  Sacrifice: "you choose one of your permanents and put it into the graveyard",
  Tap: "turn sideways to mark used or attacking—untap step stands it up again unless an effect says not",
  Connive: "draw a card, discard a card; nonland discard can grow a creature",
  Explore: "reveal top; land goes to hand, otherwise +1/+1 counter and nonland to graveyard",
  Landfall: "fires each time a land enters under your control—order on stack can matter",
  Raid: "bonus only if you attacked with a creature earlier this turn",
  Morbid: "bonus only if a creature already died this turn before the check",
  Revolt: "bonus only if your permanent left the battlefield this turn",
  Delirium: "bonus if your graveyard has four or more different card types",
  Threshold: "bonus while your graveyard has seven or more cards—unless the card says otherwise",
  Constellation: "fires when any enchantment enters under your control",
  Battalion: "fires when this attacks alongside at least two other attackers",
  "Pack tactics": "at start of combat, bonus if your attackers total power 6+",
  Heroic: "fires when you cast a spell that targets this creature",
  Magecraft: "fires when you cast or copy an instant or sorcery",
  Metalcraft: "bonus while you control three or more artifacts",
  Ferocious: "bonus while you control a creature with power 4 or greater",
  Formidable: "bonus while your creatures total power 8 or greater",
  Converge: "effect scales with how many colors of mana you spent to cast the spell",
  Domain: "effect scales with how many basic land types your lands have—max five",
  Surge: "spell may cost less if you or a teammate already cast another spell this turn",
  Spectacle: "alternate cost if an opponent lost life this turn",
  Adamant: "extra reward if three mana of one color went into casting this",
  Addendum: "extra reward if you cast it during your main phase",
  Lieutenant: "bonus while your commander is on the battlefield under your control",
  Eminence: "works from command zone even before you cast the commander—read card carefully",
  Landship: "counts how many lands entered under you this turn",
};
