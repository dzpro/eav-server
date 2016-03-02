/*
   Mittwoch, 2. März 201620:50:23
   User: 
   Server: .\SQLExpress
   Database: 2flex 2Sexy Content
   Application: 
*/

/* To prevent any potential data loss issues, you should review this script in detail before running it outside the context of the database designer.*/
BEGIN TRANSACTION
SET QUOTED_IDENTIFIER ON
SET ARITHABORT ON
SET NUMERIC_ROUNDABORT OFF
SET CONCAT_NULL_YIELDS_NULL ON
SET ANSI_NULLS ON
SET ANSI_PADDING ON
SET ANSI_WARNINGS ON
COMMIT
BEGIN TRANSACTION
GO
IF NOT EXISTS (SELECT *  FROM   sys.columns 
  WHERE  object_id = OBJECT_ID(N'[dbo].[ToSIC_EAV_Entities]') 
         AND name = 'Owner'
)
BEGIN
ALTER TABLE dbo.ToSIC_EAV_Entities ADD
	Owner nvarchar(250) NULL
END
GO
ALTER TABLE dbo.ToSIC_EAV_Entities SET (LOCK_ESCALATION = TABLE)
GO
COMMIT
