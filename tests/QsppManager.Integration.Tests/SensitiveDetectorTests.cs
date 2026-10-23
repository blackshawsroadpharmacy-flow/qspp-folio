using QsppManager.Infrastructure.ProcessSort;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>Sensitive-detector tuning against the REAL pharmacy corpus (24 Oct: 33
/// distinct held docs were replayed — 19 cleared as policies/guides/templates
/// ABOUT the topics, 14 kept as records). Each test case below mirrors a REAL
/// document from the corpus or a true-positive guard.</summary>
public class SensitiveDetectorTests
{
    [Theory]
    [InlineData("Access setup: password: Tr0ub4dor&3 assigned per user", "access-sheet.txt")]
    [InlineData("Primary passphrase = orange moon tide vault", "notes.txt")]
    [InlineData("SECRET: AKIAIOSFODNN7EXAMPLE for the reporting integration", "config.txt")]
    public void CredentialValue_IsHeld_WhateverTheTitle(string text, string fileName)
    {
        var reasons = SensitiveDetector.Detect(text, fileName);
        Assert.Contains(reasons, r => r.Contains("credential"));
    }

    [Theory]
    [InlineData("Each authorised user will be provided with a unique username and a secure password. User access is revoked on separation.",
        "My Health Record (MHR) Security and Access Policy.docx")]
    [InlineData("Password Management: passwords must be 12+ characters and rotated quarterly. Credentials are never shared between staff.",
        "IT Security & Access Policy & Procedure.docx")]
    [InlineData("You will receive login details for the WorkCover online portal and can reset your password at any time.",
        "WorkCover - Registration 2025 - Blackshaws Road Pharmacy.pdf")]
    public void SecurityPoliciesAndRegistrations_Pass_BareWordMentionsAreNotSecrets(string text, string fileName)
    {
        Assert.Empty(SensitiveDetector.Detect(text, fileName));
    }

    [Theory]
    [InlineData("Plan your roster before the visit. Bring rostering evidence, payroll records and the induction checklist.",
        "Onsite-Assessment-Guide-15072024.pdf")]
    [InlineData("Staff must follow the uniform policy; roster changes go through the manager. Payroll queries weekly.",
        "Appearance & Behaviour Uniform Policy & Procedure.docx")]
    [InlineData("Documents still needed: Annual Performance Review Form, Roster (current & archived), training records.",
        "QCPP2025 - Documents To Do List.docx")]
    public void GuidesAndToDoLists_ThatMerelyCategorise_Pass(string text, string fileName)
    {
        Assert.Empty(SensitiveDetector.Detect(text, fileName));
    }

    [Theory]
    [InlineData("Employment duties, roster allocation and performance expectations for the role.", "New Employee Standard Induction Procedure.docx")]
    [InlineData("Employment contract template with payroll and disciplinary clauses to adapt per store.", "Employment-Contract-Template_FT-and-PT-2024.docx")]
    [InlineData("Induction checklist covering roster access, payroll setup and credential guidance for new staff.",
        "Burke Road Compounding Pharmacy - New Employee Induction Checklist.pdf")]
    public void TemplatesAndProcedures_AboutWorkforceTopics_Pass(string text, string fileName)
    {
        Assert.Empty(SensitiveDetector.Detect(text, fileName));
    }

    [Theory]
    [InlineData("Candidate CV. Previous employer rostered her across 7-day community pharmacy, payroll reference available.",
        "Sophie Hannebery - CV.pdf")]
    [InlineData("Formal warning issued under the disciplinary policy for repeated performance failures this quarter.",
        "Anna Nguyen - Formal Warning 03-06-2025.docx")]
    [InlineData("Signed employment contract including payroll classification and disciplinary provisions.",
        "Virginia Elise Walker - Employment Contract -signed.pdf")]
    [InlineData("Wage agreement settled at the annualised salary for rostered weekend work.", "Annualised-Wage-Agreement-2023.docx")]
    public void PersonnelRecords_Hold_EvenWhenTheyMentionPolicies(string text, string fileName)
    {
        // the policy words in the BODY must not exempt a record — the shape is judged
        // by the FILE NAME only
        var reasons = SensitiveDetector.Detect(text, fileName);
        Assert.Contains(reasons, r => r.StartsWith("personnel"));
    }

    [Fact]
    public void PatientConfidentialMarker_UnchangedStillHolds()
    {
        // wording as the real referral-list documents carry it: the narrow
        // confidential word pairs with a patient-word on one line
        var reasons = SensitiveDetector.Detect(
            "REFERRAL LIST - CONFIDENTIAL. Each patient referral carries the consumer's MRN and medication history.",
            "OSP Referral List 2025.docx");
        Assert.Contains(reasons, r => r.StartsWith("patient"));
    }

    [Fact]
    public void ConfidentialityPolicies_AreNotPatientRecords()
    {
        // the widening that included "confidentiality" was REVERTED: a policy ABOUT
        // confidential handling of health information is evidence, not a record
        Assert.Empty(SensitiveDetector.Detect(
            "Patient confidentiality obligations: each consumer record is accessed under the access-control matrix.",
            "IT Security & Access Policy & Procedure.docx"));
    }

    [Fact]
    public void MedicarePairing_UnchangedStillHolds()
    {
        var reasons = SensitiveDetector.Detect(
            "Medicare card numbers recorded for this patient: 4111 1111 1 plus confidential clinical notes.",
            "medicare-batch-report.pdf");
        Assert.Contains(reasons, r => r.StartsWith("a Medicare-number"));
    }

    [Fact]
    public void RosterNoteWord_DoesNotTriggerPersonnel()
    {
        Assert.Empty(SensitiveDetector.Detect("attach the roster note to the daily schedule", "daily-plan.txt"));
    }

    [Fact]
    public void BlankText_YieldsNoReasons()
    {
        Assert.Empty(SensitiveDetector.Detect("", "anything.pdf"));
        Assert.Empty(SensitiveDetector.Detect(null, "anything.pdf"));
    }
}