using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindMatchAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class MakeInterviewAnswerLinksRequired : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InterviewAnswers_InterviewQuestions_QuestionId",
                table: "InterviewAnswers");

            migrationBuilder.DropForeignKey(
                name: "FK_InterviewAnswers_Interviews_InterviewId",
                table: "InterviewAnswers");

            migrationBuilder.AlterColumn<Guid>(
                name: "QuestionId",
                table: "InterviewAnswers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "InterviewId",
                table: "InterviewAnswers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ContactEmailHash",
                table: "Companies",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ContactEmailEncrypted",
                table: "Companies",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ContactEmail",
                table: "Companies",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "EmailHash",
                table: "Candidates",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "EmailEncrypted",
                table: "Candidates",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Candidates",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_InterviewAnswers_InterviewQuestions_QuestionId",
                table: "InterviewAnswers",
                column: "QuestionId",
                principalTable: "InterviewQuestions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_InterviewAnswers_Interviews_InterviewId",
                table: "InterviewAnswers",
                column: "InterviewId",
                principalTable: "Interviews",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InterviewAnswers_InterviewQuestions_QuestionId",
                table: "InterviewAnswers");

            migrationBuilder.DropForeignKey(
                name: "FK_InterviewAnswers_Interviews_InterviewId",
                table: "InterviewAnswers");

            migrationBuilder.AlterColumn<Guid>(
                name: "QuestionId",
                table: "InterviewAnswers",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "InterviewId",
                table: "InterviewAnswers",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "ContactEmailHash",
                table: "Companies",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "ContactEmailEncrypted",
                table: "Companies",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "ContactEmail",
                table: "Companies",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "EmailHash",
                table: "Candidates",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "EmailEncrypted",
                table: "Candidates",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Candidates",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddForeignKey(
                name: "FK_InterviewAnswers_InterviewQuestions_QuestionId",
                table: "InterviewAnswers",
                column: "QuestionId",
                principalTable: "InterviewQuestions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_InterviewAnswers_Interviews_InterviewId",
                table: "InterviewAnswers",
                column: "InterviewId",
                principalTable: "Interviews",
                principalColumn: "Id");
        }
    }
}
