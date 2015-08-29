﻿using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;

namespace GoogleTestAdapter
{
    [ExtensionUri(ExecutorUriString)]
    public class GoogleTestExecutor : AbstractGoogleTestAdapterClass, ITestExecutor
    {
        public const string ExecutorUriString = Constants.identifierUri;
        public static readonly Uri ExecutorUri = new Uri(ExecutorUriString);

        private bool Canceled = false;

        public void Cancel()
        {
            DebugUtils.CheckDebugModeForExecutionCode();

            Canceled = true;
        }

        public void RunTests(IEnumerable<string> sources, IRunContext runContext, IFrameworkHandle frameworkHandle)
        {
            DebugUtils.CheckDebugModeForExecutionCode(frameworkHandle);

            Canceled = false;
            foreach (string executable in sources)
            {
                if (Canceled)
                {
                    break;
                }

                GoogleTestDiscoverer Discoverer = new GoogleTestDiscoverer();
                List<TestCase> AllCases = Discoverer.GetTestsFromExecutable(frameworkHandle, executable);
                RunTests(AllCases, AllCases, runContext, frameworkHandle, true);
            }
        }

        public void RunTests(IEnumerable<TestCase> tests, IRunContext runContext, IFrameworkHandle frameworkHandle)
        {
            DebugUtils.CheckDebugModeForExecutionCode(frameworkHandle);

            Canceled = false;
            List<TestCase> AllTestCasesInAllExecutables = new List<TestCase>();
            HashSet<string> executables = new HashSet<string>();
            foreach (TestCase testcase in tests)
            {
                executables.Add(testcase.Source);
            }

            foreach (string executable in executables)
            {
                AllTestCasesInAllExecutables.AddRange(new GoogleTestDiscoverer().GetTestsFromExecutable(frameworkHandle, executable));
            }
            RunTests(AllTestCasesInAllExecutables, tests, runContext, frameworkHandle, false);
        }

        private void RunTests(IEnumerable<TestCase> allCases, IEnumerable<TestCase> cases, IRunContext runContext, IFrameworkHandle handle, bool runAll)
        {
            Dictionary<string, List<TestCase>> groupedCases = new Dictionary<string, List<TestCase>>();
            foreach (TestCase testcase in cases)
            {
                List<TestCase> group;
                if (groupedCases.ContainsKey(testcase.Source))
                {
                    group = groupedCases[testcase.Source];
                }
                else
                {
                    group = new List<TestCase>();
                    groupedCases.Add(testcase.Source, group);
                }
                group.Add(testcase);
            }

            foreach (string executable in groupedCases.Keys)
            {
                if (Canceled)
                {
                    break;
                }
                try
                {
                    RunTestsFromExecutable(handle, runContext, allCases, groupedCases[executable], executable, runAll);
                }
                catch (Exception e)
                {
                    handle.SendMessage(TestMessageLevel.Error, e.Message);
                    handle.SendMessage(TestMessageLevel.Error, e.StackTrace);
                }
            }

        }

        private void RunTestsFromExecutable(IFrameworkHandle handle, IRunContext runContext, IEnumerable<TestCase> allCases, IEnumerable<TestCase> cases, string executable, bool runAll)
        {
            foreach (TestCase testcase in cases)
            {
                handle.RecordStart(testcase);
            }

            string OutputPath = Path.GetTempFileName();
            string WorkingDir = Path.GetDirectoryName(executable);
            foreach(string Arguments in new GoogleTestCommandLine(runAll, executable.Length, allCases, cases, OutputPath, handle, Options).GetCommandLines())
            {
                List<string> ConsoleOutput = ProcessUtils.GetOutputOfCommand(handle, WorkingDir, executable, Arguments, Options.PrintTestOutput, false);
                foreach (TestResult testResult in CollectTestResults(OutputPath, cases, ConsoleOutput, handle))
                {
                    handle.RecordResult(testResult);
                }
            }
        }

        private List<TestResult> CollectTestResults(string outputPath, IEnumerable<TestCase> cases, List<string> consoleOutput, IFrameworkHandle handle)
        {
            List<TestResult> TestResults = new List<TestResult>();

            GoogleTestResultXmlParser XmlParser = new GoogleTestResultXmlParser(outputPath, cases, handle);
            TestResults.AddRange(XmlParser.GetTestResults());

            if (TestResults.Count < cases.Count())
            {
                GoogleTestResultStandardOutputParser ConsoleParser = new GoogleTestResultStandardOutputParser(consoleOutput, cases, handle);
                List<TestResult> ConsoleResults = ConsoleParser.GetTestResults();
                foreach (TestResult testResult in ConsoleResults.Where(tr => !TestResults.Exists(tr2 => tr.TestCase.FullyQualifiedName == tr2.TestCase.FullyQualifiedName)))
                {
                    TestResults.Add(testResult);
                }

                if (TestResults.Count < cases.Count())
                {
                    foreach (TestCase testcase in cases.Where(c => !TestResults.Exists(tr => tr.TestCase.FullyQualifiedName == c.FullyQualifiedName)))
                    {
                        string ErrorMsg = ConsoleParser.CrashedTestCase == null ? ""
                            : "probably crash of test " + ConsoleParser.CrashedTestCase.DisplayName;
                        TestResults.Add(new TestResult(testcase)
                        {
                            ComputerName = Environment.MachineName,
                            Outcome = TestOutcome.NotFound,
                            ErrorMessage = ErrorMsg
                        });
                    }
                }
            }

            return TestResults;
        }

    }

}