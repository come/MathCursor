# Audit des macros LaTeX émises par le core MathCursor

Source : extraction `templates:` + `examples.output:` de tous les YAML sous `data\yaml_domains`.

> **Mise à jour 2026-05-06** — Audit révisé après probe-tests
> [`adapter-vsto/tests/MathCursor.Tests/UI/WpfMathRenderProbeTests.cs`](../adapter-vsto/tests/MathCursor.Tests/UI/WpfMathRenderProbeTests.cs)
> (cf. brief [`docs/dev/briefs/2026-05-06-wpfmath-fallback-renderer.md`](../docs/dev/briefs/2026-05-06-wpfmath-fallback-renderer.md)).
> Signal `50×50 / 207 bytes` du PNG = WpfMath rend un glyphe placeholder ".".
> 6 macros marquées "manquantes" rendaient en fait correctement, et la
> stratégie de substitution Unicode dans `WpfMathAdapter` était buggée pour
> `\iint` / `\iiint` (substituait en caractères Unicode que WpfMath ne sait
> pas rendre). Voir tableau "Résolution" ci-dessous.

## Résolution par macro (2026-05-06)

| Macro | Statut | Résolution |
|---|---|---|
| `\mathbb{X}` | ❌ Vraiment cassée | `MixedLatexRenderer` substitue en TextBlock Unicode (ℝ ℕ ℤ ℚ ℂ ℙ) avec Cambria Math. Fallback nesté : `WpfMathAdapter` → `\|X`. |
| `\mapsto` | ❌ Vraiment cassée | `MixedLatexRenderer` substitue en TextBlock Unicode ↦. Fallback nesté : `WpfMathAdapter` → `\to`. |
| `\iint`, `\iiint` | ❌ Vraiment cassées | `MixedLatexRenderer` substitue en TextBlock Unicode ∬ / ∭. |
| `\setminus` | ✅ OK via subst LaTeX | `WpfMathAdapter` → `\backslash` (rendu correct). |
| `\widehat{X}` | ✅ OK via subst LaTeX | `WpfMathAdapter` → `\hat{X}` (perd le multi-char extension, acceptable). |
| `\overline{X}` | ✅ OK via subst LaTeX | `WpfMathAdapter` → `\bar{X}` (idem). |
| `\oint` | ✅ Rendue nativement | Pass-through, pas de subst nécessaire. |
| `\limsup`, `\liminf` | ✅ OK via décomposition | `WpfMathAdapter` → `\lim\sup` / `\lim\inf`. |
| `\begin{cases}…\end{cases}` | ✅ OK via décomposition | `WpfMathAdapter` → `\stackrel` imbriqués. |
| `\begin{pmatrix}…\end{pmatrix}` | ✅ OK via décomposition | `WpfMathAdapter` → `\binom` (2 lignes) / `\genfrac` (3+). |
| `\begin{bmatrix}/\{vmatrix}` | ✅ OK via décomposition | Idem. |
| `\mid` | ✅ Confirmé supportée | Vérifié, pas de problème. |

## Manquantes WpfMath (audit historique — partiellement obsolète)

> Liste d'origine. Les macros marquées ✅ ci-dessus ne sont en fait PAS manquantes.

| Macro | Count | Sources (sample) |
|---|---|---|
| `\begin` | 3 | fr/_language.yaml::system_fr, fr/_language.yaml::system_fr::out |
| `\end` | 3 | fr/_language.yaml::system_fr, fr/_language.yaml::system_fr::out |
| `\iint` | 2 | shared/symbolic.yaml::double_integral, shared/symbolic.yaml::double_integral::out |
| `\liminf` | 2 | fr/_language.yaml::liminf_expr, fr/_language.yaml::liminf_expr::out |
| `\limsup` | 2 | fr/_language.yaml::limsup_expr, fr/_language.yaml::limsup_expr::out |
| `\mapsto` | 4 | shared/symbolic.yaml::function_mapsto, shared/symbolic.yaml::function_mapsto::out |
| `\mathbb` | 46 | en/_language.yaml::forall_en::out, fr/_language.yaml::belongs_shorthand_unified::out, fr/_language.yaml::belongs_to_fr::out _(+19)_ |
| `\oint` | 2 | shared/symbolic.yaml::contour_integral, shared/symbolic.yaml::contour_integral::out |
| `\overline` | 7 | shared/symbolic.yaml::mean_overline_glued, shared/symbolic.yaml::mean_overline_glued::out, shared/symbolic.yaml::mean_overline_spaced _(+1)_ |
| `\setminus` | 7 | fr/_language.yaml::belongs_shorthand_unified::out, fr/_language.yaml::forall_shorthand_unified_spaced::out, shared/symbolic.yaml::set_minus_singleton_dash _(+3)_ |
| `\widehat` | 2 | fr/geometry.yaml::unit_angle, fr/geometry.yaml::unit_angle::out |

## Inconnues — à vérifier (1)

> Pas dans ma liste WpfMath supportée — peut-être supportées en réalité, à confirmer en testant sur la version qu'on utilise.

| Macro | Count | Sources (sample) |
|---|---|---|
| `\mid` | 6 | en/_language.yaml::exists_en, en/_language.yaml::exists_en::out, fr/_language.yaml::exists_fr _(+3)_ |

## Supportées par WpfMath (47)

> Couvertes nativement, rien à faire.

| Macro | Count | Sources (sample) |
|---|---|---|
| `\Omega` | 2 | shared/symbolic.yaml::landau_omega, shared/symbolic.yaml::landau_omega::out |
| `\Theta` | 2 | shared/symbolic.yaml::landau_theta, shared/symbolic.yaml::landau_theta::out |
| `\alpha` | 2 | en/_language.yaml::exists_en::out, fr/_language.yaml::exists_fr::out |
| `\arccos` | 3 | shared/symbolic.yaml::named_function_arccos, shared/symbolic.yaml::named_function_arccos::out |
| `\arcsin` | 3 | shared/symbolic.yaml::named_function_arcsin, shared/symbolic.yaml::named_function_arcsin::out |
| `\arctan` | 3 | shared/symbolic.yaml::named_function_arctan, shared/symbolic.yaml::named_function_arctan::out |
| `\binom` | 20 | fr/geometry.yaml::vector_components_2d, fr/geometry.yaml::vector_components_2d::out, fr/geometry.yaml::vector_components_2d_upper _(+11)_ |
| `\cap` | 4 | fr/probability.yaml::probability_intersection, fr/probability.yaml::probability_intersection::out, shared/symbolic.yaml::intersection_expr _(+1)_ |
| `\cdot` | 5 | fr/geometry.yaml::dot_product, fr/geometry.yaml::dot_product::out, fr/geometry.yaml::dot_product_atomic _(+2)_ |
| `\circ` | 3 | shared/symbolic.yaml::function_composition, shared/symbolic.yaml::function_composition::out |
| `\cos` | 5 | shared/symbolic.yaml::named_function_cos, shared/symbolic.yaml::named_function_cos::out, shared/symbolic.yaml::trig_power_explicit::out _(+1)_ |
| `\cosh` | 3 | shared/symbolic.yaml::named_function_cosh, shared/symbolic.yaml::named_function_cosh::out |
| `\cot` | 2 | shared/symbolic.yaml::named_function_cot, shared/symbolic.yaml::named_function_cot::out |
| `\csc` | 1 | shared/symbolic.yaml::named_function_csc |
| `\cup` | 4 | fr/_language.yaml::belongs_shorthand_unified::out, fr/_language.yaml::forall_shorthand_unified_spaced::out, shared/symbolic.yaml::union_expr _(+1)_ |
| `\ddot` | 2 | shared/symbolic.yaml::ddot_newton, shared/symbolic.yaml::ddot_newton::out |
| `\det` | 6 | fr/geometry.yaml::determinant_2vec, fr/geometry.yaml::determinant_2vec::out, fr/geometry.yaml::determinant_2vec_upper _(+2)_ |
| `\dim` | 1 | shared/symbolic.yaml::named_operator_call::out |
| `\dot` | 2 | shared/symbolic.yaml::dot_newton, shared/symbolic.yaml::dot_newton::out |
| `\exists` | 4 | en/_language.yaml::exists_en, en/_language.yaml::exists_en::out, fr/_language.yaml::exists_fr _(+1)_ |
| `\forall` | 28 | en/_language.yaml::forall_en, en/_language.yaml::forall_en::out, fr/_language.yaml::forall_fr _(+9)_ |
| `\frac` | 12 | fr/_language.yaml::limit_one_sided_with_f::out, fr/_language.yaml::limit_with_expr_function::out, fr/analysis.yaml::derivative_leibniz _(+6)_ |
| `\gcd` | 2 | shared/symbolic.yaml::named_operator_call::out |
| `\geq` | 1 | fr/algebra.yaml::inequality::out |
| `\in` | 40 | en/_language.yaml::forall_en, en/_language.yaml::forall_en::out, fr/_language.yaml::belongs_shorthand_unified _(+11)_ |
| `\infty` | 13 | fr/_language.yaml::liminf_expr::out, fr/_language.yaml::limit_fr_qd::out, fr/_language.yaml::limit_only::out _(+6)_ |
| `\int` | 17 | fr/_language.yaml::integral_fr_bounds, fr/_language.yaml::integral_fr_bounds::out, fr/_language.yaml::integral_fr_indef _(+7)_ |
| `\ker` | 1 | shared/symbolic.yaml::named_operator_call::out |
| `\lambda` | 1 | fr/geometry.yaml::scalar_times_vector::out |
| `\lim` | 30 | en/_language.yaml::limit_en_as, en/_language.yaml::limit_en_as::out, fr/_language.yaml::limit_fr_en_point _(+15)_ |
| `\ln` | 3 | shared/symbolic.yaml::named_function_ln, shared/symbolic.yaml::named_function_ln::out, shared/symbolic.yaml::trig_power_explicit::out |
| `\log` | 2 | shared/symbolic.yaml::named_function_log, shared/symbolic.yaml::named_function_log::out |
| `\mathcal` | 3 | fr/_language.yaml::curve_shorthand, fr/_language.yaml::curve_shorthand::out |
| `\operatorname` | 14 | shared/symbolic.yaml::named_function_argch, shared/symbolic.yaml::named_function_argch::out, shared/symbolic.yaml::named_function_argsh _(+4)_ |
| `\partial` | 3 | shared/symbolic.yaml::partial_derivative, shared/symbolic.yaml::partial_derivative::out |
| `\pi` | 3 | fr/_language.yaml::integral_fr_bounds::out, fr/analysis.yaml::integral_eq_to::out, fr/analysis.yaml::integral_space_separated::out |
| `\prod` | 7 | fr/analysis.yaml::product_eq_to_factor, fr/analysis.yaml::product_eq_to_factor::out, fr/analysis.yaml::product_paren_3args _(+3)_ |
| `\sec` | 1 | shared/symbolic.yaml::named_function_sec |
| `\sin` | 8 | fr/_language.yaml::integral_fr_bounds::out, fr/_language.yaml::integral_fr_indef::out, fr/analysis.yaml::integral_eq_to::out _(+5)_ |
| `\sinh` | 3 | shared/symbolic.yaml::named_function_sinh, shared/symbolic.yaml::named_function_sinh::out |
| `\sqrt` | 17 | en/_language.yaml::sqrt_en, en/_language.yaml::sqrt_en::out, fr/_language.yaml::nth_root_bracket _(+9)_ |
| `\sum` | 10 | fr/analysis.yaml::sum_eq_cmp_summand, fr/analysis.yaml::sum_eq_cmp_summand::out, fr/analysis.yaml::sum_eq_to_summand _(+5)_ |
| `\tan` | 4 | shared/symbolic.yaml::named_function_tan, shared/symbolic.yaml::named_function_tan::out, shared/symbolic.yaml::trig_power_explicit::out |
| `\tanh` | 3 | shared/symbolic.yaml::named_function_tanh, shared/symbolic.yaml::named_function_tanh::out |
| `\to` | 34 | en/_language.yaml::limit_en_as, en/_language.yaml::limit_en_as::out, fr/_language.yaml::liminf_expr _(+19)_ |
| `\vec` | 35 | fr/_language.yaml::vector_fr, fr/_language.yaml::vector_fr::out, fr/geometry.yaml::chasles_vectors _(+25)_ |
| `\wedge` | 2 | fr/geometry.yaml::cross_product_atomic, fr/geometry.yaml::cross_product_atomic::out |

