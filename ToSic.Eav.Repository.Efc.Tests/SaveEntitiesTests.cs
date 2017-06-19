﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToSic.Eav.Data;
using ToSic.Eav.Data.Builder;
using ToSic.Eav.Interfaces;
using ToSic.Eav.Repository.Efc.Parts;

namespace ToSic.Eav.Repository.Efc.Tests
{
    [TestClass]
    public class SaveEntitiesTests
    {
        #region Test Data Person tests
        
        ContentType _ctNull = null;
        ContentType _ctPerson = new ContentType("Person", "Person") {Attributes = new List<IAttributeDefinition>
        {
            new AttributeDefinition("FullName", "String", true, 0, 0),
            new AttributeDefinition("FirstName", "String", true, 0, 0),
            new AttributeDefinition("LastName", "String", true, 0, 0),
            new AttributeDefinition("Birthday", "DateTime", true, 0, 0),
            new AttributeDefinition("Husband", "String", true, 0, 0),
            new AttributeDefinition("UnusedField", "String", true, 0,0)
        }};
        Entity _origENull = null;

        private readonly IEntity _girlSingle = new Entity(999, "", new Dictionary<string, object>
        {
            {"FullName", "Sandra Unmarried"},
            {"FirstName", "Sandra"},
            {"LastName", "Unmarried"},
            {"Birthday", new DateTime(1981, 5, 14) }
        });

        private readonly IEntity _girlMarried = new Entity(0, "", new Dictionary<string, object>
        {
            {"FullName", "Sandra Unmarried-Married"},
            {"FirstName", "Sandra"},
            {"LastName", "Unmarried-Married"},
            {"Husband", "HusbandName" },
            {"SingleName", "Unmarried" },
            {"WeddingDate", DateTime.Today }
        });

        private readonly IEntity _girlMarriedUpdate = new Entity(0, "", new Dictionary<string, object>
        {
            {"FullName", "Sandra Unmarried-Married"},
            //{"FirstName", "Sandra"},
            {"LastName", "Unmarried-Married"},
            {"Husband", "HusbandName" },
            {"SingleName", "Unmarried" },
            {"WeddingDate", DateTime.Today }
        });



        #endregion

        #region languge definitions
        private static Dimension langEn = new Dimension {DimensionId = 1, Key = "en-US"};
        private static Dimension langDeDe = new Dimension {DimensionId = 42, Key = "de-DE"};
        private static Dimension langDeCh = new Dimension {DimensionId = 39, Key = "de-CH"};
        private static Dimension langFr = new Dimension {DimensionId = 99, Key = "fr-FR"};
        private static List<ILanguage> activeLangs = new List<ILanguage> {langEn, langDeDe, langDeCh};
        #endregion

        #region build SaveOptions as needed
        readonly SaveOptions _saveDefault = CreateOptions();
        readonly SaveOptions _saveKeepAttribs = CreateOptions(PreserveExistingAttributes: true);
        readonly SaveOptions _saveKeepAndClean = CreateOptions(PreserveExistingAttributes: true, PreserveUnknownAttributes: false);
        readonly SaveOptions _saveClean = CreateOptions( PreserveUnknownAttributes: false);
        readonly SaveOptions _saveKeepLangs = CreateOptions( PreserveUnknownLanguages: true);

        private static SaveOptions CreateOptions(bool? PreserveExistingAttributes = null, bool? PreserveUnknownAttributes = null, bool? PreserveUnknownLanguages = null)
        {
            var x = new SaveOptions
            {
                PrimaryLanguage = langEn.Key,
                Languages = new List<ILanguage> { langEn, langDeDe, langDeCh }
            };
            if (PreserveExistingAttributes != null)
                x.PreserveExistingAttributes = PreserveExistingAttributes.Value;
            if (PreserveUnknownAttributes != null)
                x.PreserveUnknownAttributes = PreserveUnknownAttributes.Value;
            if (PreserveUnknownLanguages != null)
                x.PreserveUnknownLanguages = PreserveUnknownLanguages.Value;

            return x;
        }

        #endregion

        #region Test Data ML

        private ContentType CtMlProduct = new ContentType("Product")
        {
            Attributes = new List<IAttributeDefinition>
            {
                new AttributeDefinition("Title", "String", true, 0, 0),
                new AttributeDefinition("Teaser", "String", false, 0, 0),
                new AttributeDefinition("Image", "Hyperlink", false, 0, 0),
            }
        };

        private readonly Entity _prodNull = null;
        private readonly Entity _prodNoLang = new Entity(3006, "Product", new Dictionary<string, object>()
        {
            { "Title", "Original Product No Lang" },
            { "Teaser", "Original Teaser no lang" },
            { "Image", "file:403" }
        }, "Title");

        private Entity _prodEn = ((Func<Entity>)(() =>
        {
            var title = AttributeBase.CreateTypedAttribute("Title", "String", new List<IValue>
            {
                Value.Build("String", "TitleEn, language En", new List<ILanguage> {langEn.Copy()}),
            });
            var teaser = AttributeBase.CreateTypedAttribute("Teaser", "String", new List<IValue>
            {
                Value.Build("String", "Teaser EN, lang en", new List<ILanguage> {langEn.Copy()}),
            });
            var file = AttributeBase.CreateTypedAttribute("File", "String", new List<IValue>
            {
                Value.Build("String", "Filen EN, lang en + ch RW", new List<ILanguage> {langEn.Copy() }),
            });

            return new Entity(3006, "Product", new Dictionary<string, object>
            {
                {title.Name, title},
                {teaser.Name, teaser},
                {file.Name, file}
            }, "Title");
        }))();

        private Entity _prodMl  = ((Func<Entity>)(() =>
        {
            var title = AttributeBase.CreateTypedAttribute("Title", "String", new List<IValue>
            {
                Value.Build("String", "TitleEn, language En", new List<ILanguage> {langEn.Copy()}),
                Value.Build("String", "Title DE",
                    new List<ILanguage> {langDeDe.Copy(), langDeCh.Copy(readOnly: true)}),
                Value.Build("String", "titre FR", new List<ILanguage> {langFr.Copy()})
            });

            var teaser = AttributeBase.CreateTypedAttribute("Teaser", "String", new List<IValue>
            {
                Value.Build("String", "teaser de de",
                    new List<ILanguage> {langDeDe.Copy() }),
                Value.Build("String", "teaser de CH",
                    new List<ILanguage> {langDeCh.Copy()}),
                Value.Build("String", "teaser FR", new List<ILanguage> {langFr.Copy( readOnly:true)}),
                // special test: leave EN (primary) at end of list, as this could happen in real life
                Value.Build("String", "Teaser EN, lang en", new List<ILanguage> {langEn.Copy()}),
            });
            var file = AttributeBase.CreateTypedAttribute("File", "String", new List<IValue>
            {
                Value.Build("String", "Filen EN, lang en + ch RW", new List<ILanguage> {langEn.Copy(), langDeCh.Copy()}),
                Value.Build("String", "Filele de de",
                    new List<ILanguage> {langDeDe.Copy(), langFr.Copy() }),
                Value.Build("String", "File FR", new List<ILanguage> {langFr.Copy()}),
                // special test - empty language item
                Value.Build("String", "File without language!", new List<ILanguage>()),
                Value.Build("String", "Filen EN, lang en + ch RW", new List<ILanguage> {langEn.Copy(), langDeCh.Copy()}),
            });

            return new Entity(430, "Product", new Dictionary<string, object>
            {
                {title.Name, title},
                {teaser.Name, teaser},
                {file.Name, file}
            }, "Title");
        }))();





        #endregion

        #region Test various cases of Multi-Language merge
        [TestMethod]
        public void MergeNoLangIntoNull_EnsureLangsDontGetAdded()
        {
            var merged = EntitySaver.CreateMergedForSaving(_prodNull, _prodNoLang, _ctNull, _saveDefault);

            Assert.AreEqual(1, merged["Title"].Values.Count, "should only have 1");
            var firstVal = merged["Title"].Values.First();
            Assert.AreEqual(0, firstVal.Languages.Count, "should still have no languages");
        }

        #region Test for clearing / not clearing unknown languages
        [TestMethod]
        public void MergeMlIntoNoLang_MustClearUnknownLang()
        {
            var merged = EntitySaver.CreateMergedForSaving(_prodNoLang, _prodMl, _ctNull, _saveDefault);

            Assert.AreEqual(2, merged["Title"].Values.Count, "should only have 2, no FR");
            var deVal = merged["Title"].Values.First(v => v.Languages.Any(l => l.Key == langDeDe.Key));
            Assert.AreEqual(2, deVal.Languages.Count, "should have 2 language");
        }

        [TestMethod]
        public void MergeMlIntoNoLang_DontClearUnknownLang()
        {
            var merged = EntitySaver.CreateMergedForSaving(_prodNoLang, _prodMl, _ctNull, _saveKeepLangs);

            Assert.AreEqual(3, merged["Title"].Values.Count, "should have 3, with FR");
            var deVal = merged["Title"].Values.First(v => v.Languages.Any(l => l.Key == langFr.Key));
            Assert.AreEqual(1, deVal.Languages.Count, "should have 1 language");
        }
        #endregion

        // todo
        [TestMethod]
        public void MergeEnIntoNoLang()
        {
            var merged = EntitySaver.CreateMergedForSaving(_prodNoLang, _prodEn, _ctNull, _saveDefault);

            Assert.AreEqual(1, merged["Title"].Values.Count, "should only have 1");
            var firstVal = merged["Title"].Values.First();
            Assert.AreEqual(1, firstVal.Languages.Count, "should have 1 language");
        }

        // todo
        [TestMethod]
        public void MergeNoLangIntoEn()
        {
            var merged = EntitySaver.CreateMergedForSaving(_prodEn, _prodNoLang, _ctNull, _saveDefault);

            Assert.AreEqual(1, merged.Title.Values.Count, "should only have 1");
            var firstVal = merged.Title.Values.First();
            Assert.AreEqual(1, firstVal.Languages.Count, "should still have 1 languages");
        }

        [TestMethod]
        public void MergeEnIntoNull()
        {
            var merged = EntitySaver.CreateMergedForSaving(_prodNull, _prodNoLang, _ctNull, _saveDefault);
            // note: nothing to test for now, so just leave it at this
        }


        #endregion

        #region Basic Merges - especially counting attributes and correct x-fer of primary attrib
        [TestMethod]
        public void MergeNullAndMarried()
        {
            var merged = EntitySaver.CreateMergedForSaving(_origENull, _girlMarried, _ctNull, _saveDefault);
            Assert.IsNotNull(merged, "result should never be null");
            Assert.AreEqual(_girlMarried.Attributes.Count, merged.Attributes.Count, "this test case should simply keep all values");
            AssertBasicsInMerge(_origENull, _girlMarried, merged, _girlMarried);
            Assert.AreSame(_girlMarried.Attributes, merged.Attributes, "attributes new / merged shouldn't be same object in this case");

            Assert.AreEqual(merged.GetBestValue("FullName"), _girlMarried.GetBestValue("FullName"), "full name should be that of married");
        }

        [TestMethod]
        public void MergeSingleAndMarried()
        {
            var merged = EntitySaver.CreateMergedForSaving(_girlSingle, _girlMarried, _ctNull, _saveDefault);
            Assert.IsNotNull(merged, "result should never be null");
            Assert.AreEqual(_girlMarried.Attributes.Count, merged.Attributes.Count, "this test case should simply keep all new values");
            AssertBasicsInMerge(_origENull, _girlMarried, merged, _girlSingle);
            Assert.AreNotSame(_girlMarried.Attributes, merged.Attributes, "attributes new / merged shouldn't be same");
            Assert.AreEqual(merged.GetBestValue("FullName"), _girlMarried.GetBestValue("FullName"), "full name should be that of married");
            Assert.AreNotEqual(merged.GetBestValue("FullName"), _girlSingle.GetBestValue("FullName"), "full name should be that of married");

            // Merge keeping 
            merged = EntitySaver.CreateMergedForSaving(_girlSingle, _girlMarried, _ctNull, _saveKeepAttribs);
            Assert.IsNotNull(merged, "result should never be null");
            Assert.AreNotEqual(_girlSingle.Attributes.Count, merged.Attributes.Count, "should have more than original count");
            Assert.AreNotEqual(_girlMarried.Attributes.Count, merged.Attributes.Count, "should have more than new count");
            AssertBasicsInMerge(_origENull, _girlMarried, merged, _girlSingle);
            Assert.AreNotSame(_girlMarried.Attributes, merged.Attributes, "attributes new / merged shouldn't be same object in this case");

            // Merge updating only 
            merged = EntitySaver.CreateMergedForSaving(_girlSingle, _girlMarriedUpdate, _ctNull, _saveKeepAttribs);
            Assert.IsNotNull(merged, "result should never be null");
            Assert.AreNotEqual(_girlSingle.Attributes.Count, merged.Attributes.Count, "should have more than original count");
            Assert.AreNotEqual(_girlMarried.Attributes.Count, merged.Attributes.Count, "should have more than new count");
            AssertBasicsInMerge(_origENull, _girlMarried, merged, _girlSingle);
            Assert.AreNotSame(_girlMarried.Attributes, merged.Attributes, "attributes new / merged shouldn't be same object in this case");

        }
        [TestMethod]
        public void MergeSingleAndMarriedFilterCtAttribs()
        {
            // todo: merge with type definition filter but without type
            // Merge keeping all and remove unknown attributes
            var merged = EntitySaver.CreateMergedForSaving(_girlSingle, _girlMarried, _ctPerson, _saveKeepAndClean);
            var expectedFields = new List<string> { "FullName", "FirstName", "LastName", "Birthday", "Husband" };
            Assert.IsNotNull(merged, "result should never be null");
            Assert.AreEqual(expectedFields.Count, merged.Attributes.Count, "should have only ct-field count except the un-used one");
            Assert.AreEqual(0, expectedFields.Except(merged.Attributes.Keys).Count(), "should have exactly the same fields as expected");
            AssertBasicsInMerge(_origENull, _girlMarried, merged, _girlSingle);

            // Merge keeping all and remove unknown attributes
            merged = EntitySaver.CreateMergedForSaving(_girlSingle, _girlMarried, _ctPerson, _saveClean);
            expectedFields.Remove("Birthday");
            Assert.IsNotNull(merged, "result should never be null");
            Assert.AreEqual(expectedFields.Count, merged.Attributes.Count, "should have only ct-field count except the un-used one");
            Assert.AreEqual(0, expectedFields.Except(merged.Attributes.Keys).Count(), "should have exactly the same fields as expected");
            AssertBasicsInMerge(_origENull, _girlMarried, merged, _girlSingle);

        }
        [TestMethod]
        public void MergeSingleAndMarriedFilterUnknownCt()
        {
            // todo: merge with type definition filter but without type
            // Merge keeping all and remove unknown attributes
            var merged = EntitySaver.CreateMergedForSaving(_girlSingle, _girlMarried, null, _saveKeepAndClean);
            var expectedFields = _girlMarried.Attributes.Keys.Concat(_girlSingle.Attributes.Keys).Distinct().ToList();
            Assert.IsNotNull(merged, "result should never be null");
            Assert.AreEqual(expectedFields.Count, merged.Attributes.Count, "should have only ct-field count except the un-used one");
            Assert.AreEqual(0, expectedFields.Except(merged.Attributes.Keys).Count(), "should have exactly the same fields as expected");
            AssertBasicsInMerge(_origENull, _girlMarried, merged, _girlSingle);
        }

        private static void AssertBasicsInMerge(IEntity orig, IEntity newE, IEntity merged, IEntity stateProviderE)
        {
            // make sure we really created a new object and that it's not identical to one of the originals
            Assert.AreNotSame(orig, merged, "merged shouldn't be original");
            Assert.AreNotSame(newE, merged, "merged shouldn't be new-data item");

            // make sure identity etc. are based on the identity-providing item
            Assert.AreEqual(stateProviderE.EntityId, merged.EntityId, "entityid");
            Assert.AreEqual(stateProviderE.EntityGuid, merged.EntityGuid, "guid");
            Assert.AreEqual(stateProviderE.IsPublished, merged.IsPublished, "ispublished");
            Assert.AreEqual(stateProviderE.RepositoryId, merged.RepositoryId, "repositoryid");
            Assert.AreEqual(stateProviderE.GetDraft(), merged.GetDraft(), "getdraft()");

        }

        #endregion
    }
}
