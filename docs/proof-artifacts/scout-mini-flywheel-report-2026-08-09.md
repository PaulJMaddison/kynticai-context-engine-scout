# Scout Mini-Flywheel Report - 2026-08-09

This public-safe proof uses synthetic data only. It demonstrates Context Engine - Scout local behaviour: authorised exact items enter the customer-owned data plane, relationship and attribution paths are inspectable, outcome labels are appended with dates, and a governed JSON brief is emitted for an approved consumer.

## Fixture

Subject: `visitor-synthetic-1042`

Synthetic exact items:

| Evidence ID | Source | Date | Summary |
| --- | --- | --- | --- |
| `SRC-WEB-001` | Home-page visit | 2026-08-08 | Visitor viewed the home page and pricing explainer. |
| `SRC-EMAIL-001` | Email event | 2026-08-08 | Visitor opened an authorised offer email and clicked product comparison. |
| `SRC-INTEREST-001` | Product/search interest | 2026-08-08 | Search terms showed product X and low-friction registration interest. |
| `SRC-REG-001` | Registration status | 2026-08-08 | Visitor did not complete registration. |
| `SRC-OUTCOME-T0-001` | Initial outcome history | 2026-08-01 | Similar journeys initially favoured direct product offer email. |
| `SRC-OUTCOME-T2-001` | Appended outcome history | 2026-08-07 | Similar unregistered visitors improved after prize-draw registration. |

## Ranking Movement

The probabilities below are local/basic Scout proof estimates. They are not Fortress canonical production scoring.

| Window | Outcome data | Rank 1 | Rank 2 | Rank 3 | Top-action hit rate | Brier score | JSON schema pass |
| --- | --- | --- | --- | --- | ---: | ---: | ---: |
| T0 | Initial synthetic history | `email_customer_with_x_offer` 0.61 | `offer_prize_draw_registration` 0.49 | `do_not_reply` 0.35 | 0.50 | 0.221 | 100% |
| T1 | 2026-08-05 appended outcomes | `email_customer_with_x_offer` 0.59 | `offer_prize_draw_registration` 0.57 | `do_not_reply` 0.33 | 0.58 | 0.204 | 100% |
| T2 | 2026-08-07 appended outcomes | `offer_prize_draw_registration` 0.67 | `email_customer_with_x_offer` 0.58 | `do_not_reply` 0.31 | 0.67 | 0.181 | 100% |

## Result

The local flywheel proof improves on the declared metric set after date-stamped outcomes are appended: top-action hit rate rises from `0.50` to `0.67`, Brier score falls from `0.221` to `0.181`, and the governed JSON contract continues to pass validation.

## Boundary

Scout stores the synthetic exact items, relationship context, attribution paths, outcome labels, governed brief, evidence IDs, and caveats locally. Optional Cloud/control-plane payloads must remain aggregate/control-plane only and must not store customer intelligence, exact items, evidence packs, recommendations, citation IDs, weighted signals, attribution paths, caveats, or per-entity relationship metadata. Production/private scale and canonical scoring route to Fortress.
