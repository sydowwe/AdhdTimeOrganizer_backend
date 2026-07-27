namespace MojaDigitalnaFirma.Kernel.notification.payload;

/// <summary>
/// <b>The payload PII contract — stated here once for the whole solution.</b> Every module that persists a
/// notification, reminder or job payload obeys this rule; the other payload properties in the codebase point
/// at this doc instead of paraphrasing it.
/// <para>
/// <b>Rule: a persisted payload carries entity ids and non-person scalars — never free-text person data.</b>
/// No person's name, address, phone number, e-mail, birth number or IBAN, in any language and under any
/// spelling. Non-person labels (an inventory item's name, a leave type's name, a job key) are fine.
/// </para>
/// <para>
/// <b>Why.</b> A payload row belongs to its <i>recipient</i> (HR, the manager, an Admin) — not to the person
/// named inside it — so <c>IEmployeeErasureService</c> / <c>ISubjectDataEraser</c> never touch it. A name
/// written into a payload is therefore frozen PII that survives GDPR erasure forever (Art. 5(1)(c) data
/// minimisation, Art. 17 erasure). Persisting the id instead means an anonymized employee degrades on its
/// own: display names are resolved at <i>render</i> time by <see cref="INotificationPayloadEnricher"/>, and
/// a name that no longer resolves simply renders name-less.
/// </para>
/// <para>
/// <b>How it is enforced.</b> Implementing this marker is what makes the rule structural rather than a
/// call-site convention: <see cref="INotificationService"/> accepts no loose <c>object</c>, so an anonymous
/// object with an <c>employeeName</c> field no longer compiles. A reflection guard test
/// (<c>PayloadPiiContractGuardTests</c>) then walks every implementation and fails on any property whose
/// name matches a person-data shape, so the contract also survives the properties added tomorrow.
/// </para>
/// <para>
/// Implementations are plain records living in this folder (Kernel), because producers live in many modules
/// and all of them reference Kernel. Declare the notification kind with
/// <see cref="NotificationPayloadAttribute"/> rather than a property, so the marker adds nothing to the
/// persisted JSON. Persisted JSON stays camelCase <c>jsonb</c> (<c>JsonHelper</c>).
/// </para>
/// </summary>
public interface INotificationPayload;