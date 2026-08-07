using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MojaDigitalnaFirma.Core.Approvals.domain.entity;
using MojaDigitalnaFirma.Core.Attendance.domain.model.entity;
using MojaDigitalnaFirma.Core.Attendance.domain.model.entity.employeeWorkLog;
using MojaDigitalnaFirma.Core.Attendance.domain.model.entity.leave;
using MojaDigitalnaFirma.Core.Attendance.domain.model.entity.overtimeAgreement;
using MojaDigitalnaFirma.Core.Attendance.domain.model.entity.periodLock;
using MojaDigitalnaFirma.Core.Attendance.domain.model.entity.stravovanie;
using MojaDigitalnaFirma.Core.Attendance.domain.model.view;
using MojaDigitalnaFirma.Core.CestovneNahrady.domain.entity;
using MojaDigitalnaFirma.Core.EmployeeModule.Contracts.domain.model;
using MojaDigitalnaFirma.Core.EmployeeModule.domain.model.entity;
using MojaDigitalnaFirma.Core.EmployeeModule.domain.model.entity.acknowledgment;
using MojaDigitalnaFirma.Core.EmployeeModule.domain.model.entity.employee;
using MojaDigitalnaFirma.Core.EmployeeModule.infrastructure.persistence.configuration.employee;
using MojaDigitalnaFirma.Core.Integracie.domain.entity;
using MojaDigitalnaFirma.Core.Inventory.domain.entity;
using MojaDigitalnaFirma.Core.Majetok.domain.entity;
using MojaDigitalnaFirma.Core.Notifications.domain.entity;
using MojaDigitalnaFirma.Core.OchranaUdajov.domain.entity;
using MojaDigitalnaFirma.Core.Partneri.domain.entity;
using MojaDigitalnaFirma.Core.Registratura.domain.entity;
using MojaDigitalnaFirma.Core.Reminders.domain.entity;
using MojaDigitalnaFirma.Core.Scheduler.domain.entity;
using MojaDigitalnaFirma.Core.Vehicles.domain.entity;
using MojaDigitalnaFirma.Core.Zmluvy.domain.entity;
using Sydowwe.Framework.Contracts.partneri;
using Sydowwe.Framework.Contracts.user;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.infrastructure;

namespace MojaDigitalnaFirma.Core.infrastructure.persistence;

public abstract class AppCoreDbContext(DbContextOptions options, ILoggedUserService loggedUserService, ILogger logger)
    : BaseDbContext<CoreUser>(options, loggedUserService, logger)
{
    // Employee module
    public DbSet<JobTitle> JobTitles { get; set; }
    public DbSet<EmploymentType> EmploymentTypes { get; set; }
    public DbSet<Employee> Employees { get; set; }
    public DbSet<JobTitleStockRequirement> JobTitleStockRequirements { get; set; }
    public DbSet<EmployeeStockAllocation> EmployeeStockAllocations { get; set; }
    public DbSet<EmployeeSalaryChange> EmployeeSalaryChanges { get; set; }
    public DbSet<EmployeeDocument> EmployeeDocuments { get; set; }
    public DbSet<EmployeeDependent> EmployeeDependents { get; set; }
    public DbSet<PendingStorageDeletion> PendingStorageDeletions { get; set; }
    public DbSet<DisciplinaryAction> DisciplinaryActions { get; set; }
    public DbSet<PerformanceReview> PerformanceReviews { get; set; }
    public DbSet<PerformanceGoal> PerformanceGoals { get; set; }
    public DbSet<ChecklistTemplate> ChecklistTemplates { get; set; }
    public DbSet<ChecklistTemplateItem> ChecklistTemplateItems { get; set; }
    public DbSet<EmployeeChecklist> EmployeeChecklists { get; set; }
    public DbSet<EmployeeChecklistItem> EmployeeChecklistItems { get; set; }

    // Employee module — acknowledgments slice (smernice / poučenia compliance trail)
    public DbSet<PolicyDocument> PolicyDocuments { get; set; }
    public DbSet<PolicyDocumentVersion> PolicyDocumentVersions { get; set; }
    public DbSet<PolicyAssignment> PolicyAssignments { get; set; }
    public DbSet<EmployeeAcknowledgment> EmployeeAcknowledgments { get; set; }

    // Attendance module
    public DbSet<AttendanceView> AttendanceView { get; set; }
    public DbSet<Leave> Leaves { get; set; }
    public DbSet<LeaveType> LeaveTypes { get; set; }
    public DbSet<LeaveBalance> LeaveBalances { get; set; }

    public DbSet<PublicHoliday> PublicHolidays { get; set; }

    public DbSet<AttendanceCalendar> AttendanceCalendars { get; set; }

    // EMPLOYEE PORTAL: live time-clock feature (item 10) — open punch sessions awaiting clock-out.
    public DbSet<ActiveWorkSession> ActiveWorkSessions { get; set; }
    public DbSet<OvertimeAgreement> OvertimeAgreements { get; set; }
    public DbSet<AttendancePeriodLock> AttendancePeriodLocks { get; set; }

    // Stravovanie (meal contribution) — §152 ZP slice inside Attendance.
    public DbSet<MealBenefitAssignment> MealBenefitAssignments { get; set; }
    public DbSet<MealRate> MealRates { get; set; }

    // Inventory (Core module — applies to every deployment)
    public DbSet<StockType> StockTypes { get; set; }
    public DbSet<Location> Locations { get; set; }
    public DbSet<UnitOfMeasure> UnitsOfMeasure { get; set; }
    public DbSet<Supplier> Suppliers { get; set; }
    public DbSet<CustomFieldDefinition> CustomFieldDefinitions { get; set; }
    public DbSet<StockItem> StockItems { get; set; }
    public DbSet<StockBatch> StockBatches { get; set; }
    public DbSet<StockLevel> StockLevels { get; set; }
    public DbSet<StockMovement> StockMovements { get; set; }

    // Notifications (Core module — applies to every deployment)
    public DbSet<Notification> Notifications { get; set; }
    public DbSet<PushSubscription> PushSubscriptions { get; set; }
    public DbSet<NotificationPreference> NotificationPreferences { get; set; }

    // The single per-user quiet-hours window for the deployment. Owned by Notifications (it defers the
    // background channels); Reminders honours the same row through the Kernel IQuietHoursReader seam, having
    // dropped its own parallel ReminderQuietHours table in notifications follow-up 05.
    public DbSet<NotificationQuietHours> NotificationQuietHours { get; set; }

    // Registratúra (správa registratúry) module
    public DbSet<RegistratoryProfile> RegistratoryProfiles { get; set; }
    public DbSet<RegistratoryOrder> RegistratoryOrders { get; set; }
    public DbSet<RegistratoryPlan> RegistratoryPlans { get; set; }
    public DbSet<RegistratoryClassEntry> RegistratoryClassEntries { get; set; }
    public DbSet<RegistratoryStorageLocation> RegistratoryStorageLocations { get; set; }
    public DbSet<RegistratoryFile> RegistratoryFiles { get; set; }
    public DbSet<RegistratoryRecord> RegistratoryRecords { get; set; }
    public DbSet<DisposalProceeding> DisposalProceedings { get; set; }
    public DbSet<DisposalItem> DisposalItems { get; set; }
    public DbSet<RegistratoryAuditEvent> RegistratoryAuditEvents { get; set; }
    public DbSet<DennikPeriodLock> DennikPeriodLocks { get; set; }

    // Scheduler (Core module — the always-on time substrate; applies to every deployment)
    public DbSet<ScheduledJob> ScheduledJobs { get; set; }
    public DbSet<ScheduledJobRun> ScheduledJobRuns { get; set; }

    // Approvals (Core module — the always-on generic approval envelope; applies to every deployment)
    public DbSet<ApprovalRequest> ApprovalRequests { get; set; }
    public DbSet<ApprovalRequestApprover> ApprovalRequestApprovers { get; set; }
    public DbSet<ApprovalDecision> ApprovalDecisions { get; set; }
    public DbSet<ApprovalDelegation> ApprovalDelegations { get; set; }

    // Ochrana osobných údajov / GDPR (Core module — always-on governance register; every deployment)
    public DbSet<ProcessingActivity> ProcessingActivities { get; set; }
    public DbSet<DataSubjectRequest> DataSubjectRequests { get; set; }
    public DbSet<DataSubjectRequestEvent> DataSubjectRequestEvents { get; set; }
    public DbSet<ConsentText> ConsentTexts { get; set; }
    public DbSet<ConsentRecord> ConsentRecords { get; set; }
    public DbSet<ConsentEvent> ConsentEvents { get; set; }
    public DbSet<DataBreach> DataBreaches { get; set; }
    public DbSet<DataBreachEvent> DataBreachEvents { get; set; }
    public DbSet<ProcessorAgreement> ProcessorAgreements { get; set; }
    public DbSet<AuthorizedPerson> AuthorizedPersons { get; set; }

    // Reminders (Core module — the always-on scheduled-reminder registry; applies to every deployment)
    public DbSet<ReminderDefinition> ReminderDefinitions { get; set; }
    public DbSet<ReminderRecipient> ReminderRecipients { get; set; }
    public DbSet<ReminderLeadOffset> ReminderLeadOffsets { get; set; }
    public DbSet<ReminderDispatch> ReminderDispatches { get; set; }
    public DbSet<ReminderKindPreference> ReminderKindPreferences { get; set; }
    public DbSet<ReminderOccurrenceAction> ReminderOccurrenceActions { get; set; }

    // Partneri (Core module — shared partner master data; applies to every deployment)
    public DbSet<Partner> Partners { get; set; }
    public DbSet<PartnerContact> PartnerContacts { get; set; }
    public DbSet<PartnerBankAccount> PartnerBankAccounts { get; set; }

    // Vehicles (Core module — vehicle register + Slovak kniha jázd)
    public DbSet<Vehicle> Vehicles { get; set; }
    public DbSet<TripLog> TripLogs { get; set; }
    public DbSet<FuelRecord> FuelRecords { get; set; }
    public DbSet<OdometerSnapshot> OdometerSnapshots { get; set; }
    public DbSet<VehicleDriverAssignment> VehicleDriverAssignments { get; set; }
    public DbSet<VehiclePeriodLock> VehiclePeriodLocks { get; set; }

    // Majetok (Core module — fixed-asset register / evidencia majetku)
    public DbSet<Asset> Assets { get; set; }
    public DbSet<AssetMovement> AssetMovements { get; set; }
    public DbSet<Stocktake> Stocktakes { get; set; }
    public DbSet<StocktakeItem> StocktakeItems { get; set; }
    public DbSet<DepreciationSnapshot> DepreciationSnapshots { get; set; }
    public DbSet<DepreciationSnapshotYear> DepreciationSnapshotYears { get; set; }

    // Zmluvy (contract lifecycle register — Core module; applies to every deployment)
    public DbSet<Contract> Contracts { get; set; }
    public DbSet<ContractAmendment> ContractAmendments { get; set; }
    public DbSet<ContractEvent> ContractEvents { get; set; }
    public DbSet<ContractObligation> ContractObligations { get; set; }
    public DbSet<ContractDocument> ContractDocuments { get; set; }

    // Cestovné náhrady (travel reimbursements) — Core module; opt-in vertical slice.
    public DbSet<TravelOrder> TravelOrders { get; set; }
    public DbSet<TripSettlement> TripSettlements { get; set; }
    public DbSet<TripMealDay> TripMealDays { get; set; }
    public DbSet<OwnCarUsage> OwnCarUsages { get; set; }
    public DbSet<TripExpenseLine> TripExpenseLines { get; set; }
    public DbSet<DomesticMealRate> DomesticMealRates { get; set; }
    public DbSet<MealReductionRate> MealReductionRates { get; set; }
    public DbSet<ForeignMealRate> ForeignMealRates { get; set; }
    public DbSet<MileageRate> MileageRates { get; set; }
    public DbSet<FuelPrice> FuelPrices { get; set; }

    // Integrácie (exports & integrations — adapter/transport layer; opt-in Core module)
    public DbSet<ConnectorConfig> ConnectorConfigs { get; set; }
    public DbSet<TransmissionLog> TransmissionLogs { get; set; }
    public DbSet<InboundDocument> InboundDocuments { get; set; }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // BaseDbContext<TUser> runs: schema, identity tables, Framework audit configs, OnModelCreatingPartial.
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AttendanceView>().ToView("materialized_view_attendance_by_days").HasNoKey();

        modelBuilder.HasSequence<int>("employee_personal_number_seq")
            .StartsAt(1000)
            .IncrementsBy(11);

        // Zmluvy: authoritative running counter for the auto-assigned ContractNumber (see
        // ContractNumberGenerator). Mirror of employee_personal_number_seq.
        modelBuilder.HasSequence<long>("contract_number_seq")
            .StartsAt(1)
            .IncrementsBy(1);

        // Core assembly: CoreUserEntityConfiguration (ToTable "user"), FK supplements for
        // RefreshToken / PushSubscription / NotificationPreference pointing at CoreUser.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CoreUser).Assembly);

        // Employee + Attendance module configs. This includes WorkLogEntityConfiguration, which
        // sets up the WorkLog TPH root (key / "work_log" table / relationships / indexes) for every
        // deployment. Customer-derived work-log types (e.g. HbWorkLog) share that table and add only
        // their own columns via a separate config picked up from GetCustomerAssemblies() below.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EmployeeEntityConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(Leave).Assembly);

        // Inventory module's EF configurations live in the Core.Inventory assembly.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StockType).Assembly);

        // Notifications module's EF configurations live in the Core.Notifications assembly.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(Notification).Assembly);

        // Registratúra module's EF configurations live in the Core.Registratura assembly.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RegistratoryProfile).Assembly);

        // Scheduler module's EF configurations live in the Core.Scheduler assembly.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ScheduledJob).Assembly);

        // Approvals module's EF configurations live in the Core.Approvals assembly.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApprovalRequest).Assembly);

        // GDPR / Ochrana osobných údajov module's EF configurations live in the Core.OchranaUdajov assembly.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProcessingActivity).Assembly);

        // Reminders module's EF configurations live in the Core.Reminders assembly.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReminderDefinition).Assembly);

        // Partneri module's EF configurations live in the Core.Partneri assembly.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(Partner).Assembly);

        // Vehicles module's EF configurations live in the Core.Vehicles assembly.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(Vehicle).Assembly);

        // Majetok module's EF configurations live in the Core.Majetok assembly.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(Asset).Assembly);

        // Zmluvy module's EF configurations live in the Core.Zmluvy assembly.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(Contract).Assembly);

        // Cestovné náhrady module's EF configurations live in the Core.CestovneNahrady assembly.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TravelOrder).Assembly);

        // Integrácie module's EF configurations live in the Core.Integracie assembly.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ConnectorConfig).Assembly);

        // Cross-module FK (Zmluvy → Partneri) configured here, at the composition seam that already sees
        // both CLR types, so neither module references the other (they couple only over Kernel contracts).
        // Navigation-less HasOne<Partner>() — no nav on either side. Restrict: a partner with contracts is
        // deactivated (soft-delete), never hard-deleted.
        modelBuilder.Entity<Contract>()
            .HasOne<Partner>()
            .WithMany()
            .HasForeignKey(c => c.PartnerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Cross-module FK (Zmluvy → Employee) configured here, at the composition seam that already sees both
        // CLR types, so Core.Zmluvy no longer references Core.EmployeeModule (they couple only over Kernel
        // contracts). Navigation-less HasOne<Employee>() — nullable gestor link, same FK shape (Restrict,
        // optional) as the former Zmluvy-side config, so no migration. The contract / obligation may have no
        // gestor; a gestor employee is deactivated (soft-delete), never hard-deleted out from under a contract.
        modelBuilder.Entity<Contract>()
            .HasOne<Employee>()
            .WithMany()
            .HasForeignKey(c => c.ResponsibleEmployeeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ContractObligation>()
            .HasOne<Employee>()
            .WithMany()
            .HasForeignKey(o => o.ResponsibleEmployeeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // Read-only SQL name seam (see the EmployeeNameRead block below). Reuses the same ResponsibleEmployeeId
        // column as the real gestor FKs above; its principal is the view-mapped EmployeeNameRead, so it emits no
        // constraint and no migration. The contract / obligation grids project this into a sortable EmployeeName.
        modelBuilder.Entity<Contract>()
            .HasOne(c => c.EmployeeNameRef)
            .WithMany()
            .HasForeignKey(c => c.ResponsibleEmployeeId)
            .IsRequired(false);

        modelBuilder.Entity<ContractObligation>()
            .HasOne(o => o.EmployeeNameRef)
            .WithMany()
            .HasForeignKey(o => o.ResponsibleEmployeeId)
            .IsRequired(false);

        // Read-only SQL name seam for the counterparty. PartnerNameRead maps onto the existing `partner`
        // table as a view (ToView → not managed by migrations, no table-sharing clash with the real Partner
        // entity, no DB object created), so Zmluvy can ORDER BY / filter the partner name in SQL while
        // referencing only Kernel contracts (never the Partneri module). Reuses the same PartnerId column as
        // the real, constraint-owning Partner FK above; its principal is the view-mapped PartnerNameRead, so
        // it emits no constraint of its own and needs no migration. The contract grid projects it into a
        // sortable/filterable PartnerName. IsRequired(false) → LEFT JOIN, defensive though PartnerId is NOT
        // NULL. See PartnerNameRead docs.
        modelBuilder.Entity<PartnerNameRead>(b =>
        {
            b.ToView("partner");
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).HasColumnName("id");
            b.Property(p => p.Name).HasColumnName("name");
        });

        modelBuilder.Entity<Contract>()
            .HasOne(c => c.PartnerNameRef)
            .WithMany()
            .HasForeignKey(c => c.PartnerId)
            .IsRequired(false);

        // Cross-module FK (Majetok → Vehicles) configured here, at the composition seam that already sees both
        // CLR types, so Core.Majetok no longer references Core.Vehicles (they couple only over Kernel contracts:
        // LinkVehicleToAssetCommand for the link write, GetVehicleEntryPricesCommand for the basis read).
        // Navigation-less HasOne<Vehicle>() — same FK shape as the former Majetok-side config, so no migration.
        // Restrict: a linked vehicle (itself soft-deletable) is never hard-deleted out from under an asset.
        modelBuilder.Entity<Asset>()
            .HasOne<Vehicle>()
            .WithMany()
            .HasForeignKey(a => a.VehicleId)
            .OnDelete(DeleteBehavior.Restrict);

        // Cross-module FK (Majetok → Employee) configured here, at the composition seam that already sees both
        // CLR types, so Core.Majetok no longer references Core.EmployeeModule (they couple only over the Kernel /
        // EmployeeModule.Contracts command seams). Navigation-less HasOne<Employee>() — nullable confirmer link
        // (the employee who confirmed the stocktake line). Restrict: a confirming employee is deactivated
        // (soft-delete), never hard-deleted out from under the inventúrny súpis history.
        modelBuilder.Entity<StocktakeItem>()
            .HasOne<Employee>()
            .WithMany()
            .HasForeignKey(i => i.ConfirmedByEmployeeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // Read-only SQL name seam (see the EmployeeNameRead block above). Reuses the same ConfirmedByEmployeeId
        // column as the real confirmer FK; its principal is the view-mapped EmployeeNameRead, so it emits no
        // constraint and no migration. The stocktake-item grid projects this into a sortable EmployeeName.
        modelBuilder.Entity<StocktakeItem>()
            .HasOne(i => i.EmployeeNameRef)
            .WithMany()
            .HasForeignKey(i => i.ConfirmedByEmployeeId)
            .IsRequired(false);

        // Nullable zodpovedná-osoba link on the asset ledger (Reassigned / PutIntoUse target). Same nav-less
        // Restrict shape — an employee assigned responsibility on a ledger row is deactivated, never hard-deleted.
        modelBuilder.Entity<AssetMovement>()
            .HasOne<Employee>()
            .WithMany()
            .HasForeignKey(m => m.ResponsibleEmployeeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // Cross-module FKs (Vehicles → Employee) configured here, at the composition seam that already sees both
        // CLR types, so Core.Vehicles no longer references Core.EmployeeModule (they couple only over Kernel /
        // EmployeeModule.Contracts: EmployeeExistsCommand for write validation, GetEmployeeSummariesCommand for
        // driver-name reads). Navigation-less HasOne<Employee>() — same FK shapes as the former in-module configs,
        // so no migration. Restrict: a driver employee is deactivated (soft-delete), never hard-deleted out from
        // under a vehicle / assignment / trip. The denormalized vehicle pointer is the only nullable one.
        modelBuilder.Entity<Vehicle>()
            .HasOne<Employee>()
            .WithMany()
            .HasForeignKey(v => v.AssignedDriverId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<VehicleDriverAssignment>()
            .HasOne<Employee>()
            .WithMany()
            .HasForeignKey(a => a.DriverId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TripLog>()
            .HasOne<Employee>()
            .WithMany()
            .HasForeignKey(t => t.DriverId)
            .OnDelete(DeleteBehavior.Restrict);

        // Cross-module FKs (Employee → Inventory) configured here, at the composition seam that already sees both
        // CLR types, so Core.EmployeeModule no longer references Core.Inventory (they couple only over the Kernel
        // GetStockItemNamesCommand / GetStockItemIdsBySkuCommand read seams). Navigation-less HasOne<StockItem>() —
        // same FK shapes (Restrict, required) as the former in-module configs, so no migration. Restrict: a catalog
        // item in use can't be hard-deleted out from under a job-title kit row or an employee allocation.
        // HasConstraintName pins the pre-decoupling constraint names (the former in-module nav config produced
        // the "stock_items" pluralized names); without them the nav-less HasOne defaults to "stock_item" and EF
        // would emit a pure rename migration despite the identical FK shape.
        modelBuilder.Entity<JobTitleStockRequirement>()
            .HasOne<StockItem>()
            .WithMany()
            .HasForeignKey(x => x.StockItemId)
            .HasConstraintName("fk_job_title_stock_requirement_stock_items_stock_item_id")
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<EmployeeStockAllocation>()
            .HasOne<StockItem>()
            .WithMany()
            .HasForeignKey(x => x.StockItemId)
            .HasConstraintName("fk_employee_stock_allocation_stock_items_stock_item_id")
            .OnDelete(DeleteBehavior.Restrict);

        // Cross-module FK (Inventory → Employee). StockMovement records the acting employee; the real,
        // constraint-owning FK lives here so Core.Inventory references only EmployeeModule.Contracts, never the
        // Employee CLR entity. Navigation-less HasOne<Employee>() — nullable actor link (the acting user may have
        // no employee record), Restrict so an employee with movement history can't be hard-deleted.
        modelBuilder.Entity<StockMovement>()
            .HasOne<Employee>()
            .WithMany()
            .HasForeignKey(x => x.PerformedByEmployeeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // Read-only SQL name seam (see the EmployeeNameRead block below). Reuses the same PerformedByEmployeeId
        // column as the real FK above; its principal is the view-mapped EmployeeNameRead, so it emits no
        // constraint of its own and needs no migration. Lets GridStockMovement sort by "EmployeeName" in SQL.
        modelBuilder.Entity<StockMovement>()
            .HasOne(x => x.EmployeeNameRef)
            .WithMany()
            .HasForeignKey(x => x.PerformedByEmployeeId)
            .IsRequired(false);

        // Cross-module FKs (Attendance → Employee) configured here, at the composition seam that already sees
        // both CLR types, so Core.Attendance no longer references Core.EmployeeModule directly (they couple only
        // over EmployeeModule.Contracts command seams). Navigation-less HasOne<Employee>() — same FK shapes as
        // the former IsManyWithOneEmployee() / HasOne(nav) configs inside Attendance, so no migration.
        modelBuilder.Entity<WorkLog>()
            .HasOne<Employee>()
            .WithMany()
            .HasForeignKey(w => w.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WorkLog>()
            .HasOne<Employee>()
            .WithMany()
            .HasForeignKey(w => w.ApprovedById)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Leave>()
            .HasOne<Employee>()
            .WithMany()
            .HasForeignKey(l => l.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Leave>()
            .HasOne<Employee>()
            .WithMany()
            .HasForeignKey(l => l.ApprovedById)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<LeaveBalance>()
            .HasOne<Employee>()
            .WithMany()
            .HasForeignKey(b => b.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ActiveWorkSession>()
            .HasOne<Employee>()
            .WithMany()
            .HasForeignKey(s => s.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<OvertimeAgreement>()
            .HasOne<Employee>()
            .WithMany()
            .HasForeignKey(a => a.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AttendancePeriodLock>()
            .HasOne<Employee>()
            .WithMany()
            .HasForeignKey(p => p.EmployeeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<MealBenefitAssignment>()
            .HasOne<Employee>()
            .WithMany()
            .HasForeignKey(m => m.EmployeeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        // Read-only SQL name seam. EmployeeNameRead maps onto the existing `employee` table as a view
        // (ToView → not managed by migrations, no table-sharing clash with the real Employee entity, no DB
        // object created). Modules that need server-side sort/filter by employee name add a navigation to it
        // and project the name inside their grid's static Projection so EF can ORDER BY it. The real
        // Employee FK above still owns the constraint/cascade; this second, view-backed relationship reuses
        // the same EmployeeId column and emits no constraint of its own. See EmployeeNameRead docs.
        modelBuilder.Entity<EmployeeNameRead>(b =>
        {
            b.ToView("employee");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.Name).HasColumnName("name");
            b.Property(e => e.Surname).HasColumnName("surname");
        });

        modelBuilder.Entity<MealBenefitAssignment>()
            .HasOne(m => m.EmployeeNameRef)
            .WithMany()
            .HasForeignKey(m => m.EmployeeId)
            .IsRequired(false);

        // Same view-backed name seam for the WorkLog / Leave HR grids (sort key "EmployeeName"). Reuses the
        // existing EmployeeId FK column; the real Employee FKs above own the constraints, so these emit none
        // and need no migration.
        modelBuilder.Entity<WorkLog>()
            .HasOne(w => w.EmployeeNameRef)
            .WithMany()
            .HasForeignKey(w => w.EmployeeId)
            .IsRequired(false);

        modelBuilder.Entity<Leave>()
            .HasOne(l => l.EmployeeNameRef)
            .WithMany()
            .HasForeignKey(l => l.EmployeeId)
            .IsRequired(false);

        // Same view-backed name seam for the Vehicles grids (driver name, sort keys "AssignedDriverName" /
        // "DriverName"). Reuses the existing AssignedDriverId / DriverId FK columns; the real Employee FKs above
        // own the constraints, so these emit none and need no migration.
        modelBuilder.Entity<Vehicle>()
            .HasOne(v => v.AssignedDriverNameRef)
            .WithMany()
            .HasForeignKey(v => v.AssignedDriverId)
            .IsRequired(false);

        modelBuilder.Entity<TripLog>()
            .HasOne(t => t.DriverNameRef)
            .WithMany()
            .HasForeignKey(t => t.DriverId)
            .IsRequired(false);

        // Cross-module FKs (CestovneNahrady → Employee) configured here, at the composition seam that already
        // sees both CLR types, so Core.CestovneNahrady no longer references Core.EmployeeModule (they couple
        // only over Kernel/EmployeeModule.Contracts: EmployeeExistsCommand for write validation,
        // GetEmployeeSummariesCommand for traveler-name reads, TravelOrderApprovalAuthorizer for manager lookup).
        // Navigation-less HasOne<Employee>() — same FK shapes as the former in-module configs, so no migration.
        // Traveler FK: Cascade (was IsManyWithOneEmployee default). Approver FKs: Restrict, nullable.
        modelBuilder.Entity<TravelOrder>()
            .HasOne<Employee>()
            .WithMany()
            .HasForeignKey(t => t.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TravelOrder>()
            .HasOne<Employee>()
            .WithMany()
            .HasForeignKey(t => t.ApprovedByEmployeeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // Read-only SQL name seam (see the EmployeeNameRead block above). Reuses the same EmployeeId column as
        // the real traveler FK; its principal is the view-mapped EmployeeNameRead, so it emits no constraint and
        // no migration. The order grid projects this directly; the settlement / trip-register grids reach it via
        // TripSettlement.TravelOrder.
        modelBuilder.Entity<TravelOrder>()
            .HasOne(t => t.EmployeeNameRef)
            .WithMany()
            .HasForeignKey(t => t.EmployeeId)
            .IsRequired(false);

        modelBuilder.Entity<TripSettlement>()
            .HasOne<Employee>()
            .WithMany()
            .HasForeignKey(s => s.ApprovedByEmployeeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // Cross-module FKs (Approvals → Employee) configured here, at the composition seam that already sees both
        // CLR types, so Core.Approvals couples to EmployeeModule only over Contracts. All approval actors are
        // Employees. Navigation-less HasOne<Employee>() — RequestedByEmployeeId is a caller-supplied explicit
        // column (the module is deliberately not IEntityWithUser, for background raises), but it must now point at
        // a real employee since the FK is enforced. Restrict on every actor FK so deleting an employee can't
        // silently orphan an approval ledger row.
        modelBuilder.Entity<ApprovalRequest>()
            .HasOne<Employee>()
            .WithMany()
            .HasForeignKey(r => r.RequestedByEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ApprovalRequest>()
            .HasOne<Employee>()
            .WithMany()
            .HasForeignKey(r => r.DecidedByEmployeeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ApprovalRequestApprover>()
            .HasOne<Employee>()
            .WithMany()
            .HasForeignKey(a => a.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ApprovalDecision>()
            .HasOne<Employee>()
            .WithMany()
            .HasForeignKey(d => d.DecidedByEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ApprovalDelegation>()
            .HasOne<Employee>()
            .WithMany()
            .HasForeignKey(d => d.FromEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ApprovalDelegation>()
            .HasOne<Employee>()
            .WithMany()
            .HasForeignKey(d => d.ToEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Read-only SQL name seam (see the EmployeeNameRead block above). Reuses the same RequestedByEmployeeId
        // column as the real requester FK; its principal is the view-mapped EmployeeNameRead, so it emits no
        // constraint of its own. The request grid projects this directly so EF can ORDER BY / filter the
        // requester's name in SQL.
        modelBuilder.Entity<ApprovalRequest>()
            .HasOne(r => r.EmployeeNameRef)
            .WithMany()
            .HasForeignKey(r => r.RequestedByEmployeeId)
            .IsRequired(false);

        foreach (var assembly in GetCustomerAssemblies())
            modelBuilder.ApplyConfigurationsFromAssembly(assembly);
    }

    // Override in customer AppCoreDbContext to register customer-specific EF configurations.
    protected virtual IEnumerable<Assembly> GetCustomerAssemblies() => [];
}

// Generic subclass: closes the WorkLog type parameter so a customer context can substitute its own
// TPH-derived work-log entity (HbWorkLog). The TPH root is still configured by the Core
// WorkLogEntityConfiguration; the derived type only adds its columns (see
// docs/extendingVanillaForCustomers.md). Core code keeps querying Set<WorkLog>() polymorphically.
public abstract class AppCoreDbContext<TWorkLog>(
    DbContextOptions options,
    ILoggedUserService loggedUserService,
    ILogger logger
) : AppCoreDbContext(options, loggedUserService, logger)
    where TWorkLog : WorkLog
{
    // Typed DbSet: callers holding a reference to the concrete customer context see
    // DbSet<HbWorkLog> rather than the base (protected) DbSet<WorkLog>.
    public DbSet<TWorkLog> WorkLogs { get; set; }
}