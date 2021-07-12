﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;
using Windows.Storage.Pickers;
using Windows.Storage;
using Windows.Graphics.Imaging;
using Windows.Media;
using Microsoft.AI.MachineLearning;
using Microsoft.UI.Xaml.Media.Imaging;

namespace WinMLSamplesGallery.Samples
{
    [ComImport, Guid("3E68D4BD-7135-4D10-8018-9FB6D9F33FA1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IInitializeWithWindow
    {
        void Initialize([In] IntPtr hwnd);
    }

    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class ImageClassifier : Page
    {
        [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Auto, PreserveSig = true, SetLastError = false)]
        public static extern IntPtr GetActiveWindow();

        private static Dictionary<long, string> labels_;
        private LearningModelSession inferenceSession_;
        private LearningModelSession preProcessingSession_;
        private LearningModelSession postProcessingSession_;

        private StorageFile currentImage_ = null;
        Model currentModel_ = Model.NoModel;
        Model loadedModel_ = Model.NoModel;
        const long BatchSize = 1;
        const long TopK = 10;
        const long NumLabels = 1000;

        private Dictionary<Model, string> modelDictionary_;
        private Dictionary<Model, Func<LearningModel>> postProcessorDictionary_;
        private Dictionary<Model, Func<LearningModel>> preProcessorDictionary_;

        private static Dictionary<long, string> imagenetLabels_;
        private static Dictionary<long, string> ilsvrc2013Labels_;

        public ImageClassifier()
        {
            this.InitializeComponent();

            AllModelsGrid.SelectedItem = null;
            AllModelsGrid.SelectionChanged += AllModelsGrid_SelectionChanged;
        }

        private void EnsureInit()
        {
            if (imagenetLabels_ == null)
            {
                imagenetLabels_ = LoadLabels("ms-appx:///InputData/sysnet.txt");
            }

            if (ilsvrc2013Labels_ == null)
            {
                ilsvrc2013Labels_ = LoadLabels("ms-appx:///InputData/ilsvrc2013.txt");
            }

            if (modelDictionary_ == null)
            {
                modelDictionary_ = new Dictionary<Model, string>{
                    { Model.SqueezeNet,        "ms-appx:///Models/squeezenet1.1-7.onnx" },
                    { Model.MobileNet,         "ms-appx:///Models/mobilenetv2-7.onnx" },
                    { Model.GoogleNet,         "ms-appx:///Models/googlenet-9.onnx"},
                    { Model.DenseNet121,       "ms-appx:///Models/densenet-9.onnx"},
                    { Model.Inception_V1,      "ms-appx:///Models/inception-v1-9.onnx"},
                    { Model.Inception_V2,      "ms-appx:///Models/inception-v2-9.onnx"},
                    { Model.ShuffleNet_V1,     "ms-appx:///Models/shufflenet-9.onnx"},
                    { Model.ShuffleNet_V2,     "ms-appx:///Models/shufflenet-v2-10.onnx"},
                    { Model.EfficientNetLite4, "ms-appx:///Models/efficientnet-lite4-11.onnx"},
                    // Large Models
                    { Model.AlexNet,           "ms-appx:///LargeModels/bvlcalexnet-9.onnx"},
                    { Model.CaffeNet,          "ms-appx:///LargeModels/caffenet-9.onnx"},
                    { Model.RCNN_ILSVRC13,     "ms-appx:///LargeModels/rcnn-ilsvrc13-9.onnx"},
                    { Model.ResNet,            "ms-appx:///LargeModels/resnet50-caffe2-v1-9.onnx"},
                    { Model.VGG,               "ms-appx:///LargeModels/vgg19-7.onnx"},
                    { Model.ZFNet512,          "ms-appx:///LargeModels/zfnet512-9.onnx"},
                };
            }

            if (postProcessorDictionary_ == null)
            {
                postProcessorDictionary_ = new Dictionary<Model, Func<LearningModel>>{
                    { Model.SqueezeNet,        () => TensorizationModels.SoftMaxThenTopK(TopK) },
                    { Model.MobileNet,         () => TensorizationModels.SoftMaxThenTopK(TopK) },
                    { Model.GoogleNet,         () => TensorizationModels.TopK(TopK) },
                    { Model.DenseNet121,       () => TensorizationModels.ReshapeThenSoftmaxThenTopK(new long[] { BatchSize, NumLabels, 1, 1 },
                                                                                                    TopK,
                                                                                                    BatchSize,
                                                                                                    NumLabels) },
                    { Model.Inception_V1,      () => TensorizationModels.TopK(TopK) },
                    { Model.Inception_V2,      () => TensorizationModels.TopK(TopK) },
                    { Model.ShuffleNet_V1,     () => TensorizationModels.TopK(TopK) },
                    { Model.ShuffleNet_V2,     () => TensorizationModels.SoftMaxThenTopK(TopK) },
                    { Model.EfficientNetLite4, () => TensorizationModels.SoftMaxThenTopK(TopK) },
                    // Large Models
                    { Model.AlexNet,           () => TensorizationModels.TopK(TopK) },
                    { Model.CaffeNet,          () => TensorizationModels.TopK(TopK) },
                    { Model.RCNN_ILSVRC13,     () => TensorizationModels.TopK(TopK) },
                    { Model.ResNet,            () => TensorizationModels.SoftMaxThenTopK(TopK) },
                    { Model.VGG,               () => TensorizationModels.SoftMaxThenTopK(TopK) },
                    { Model.ZFNet512,          () => TensorizationModels.TopK(TopK) },
                };
            }

            if (preProcessorDictionary_ == null)
            {
                preProcessorDictionary_ = new Dictionary<Model, Func<LearningModel>>{
                    { Model.SqueezeNet,        null }, // No preprocessing required
                    { Model.MobileNet,         () => TensorizationModels.Normalize0_1ThenZScore(224, 224, 4,
                                                                                                new float[] { 0.485f, 0.456f, 0.406f },
                                                                                                new float[] { 0.229f, 0.224f, 0.225f}) },
                    { Model.GoogleNet,         null },
                    { Model.DenseNet121,       () => TensorizationModels.Normalize0_1ThenZScore(224, 224, 4,
                                                                                                new float[] { 0.485f, 0.456f, 0.406f },
                                                                                                new float[] { 0.229f, 0.224f, 0.225f}) },
                    { Model.Inception_V1,      null }, // No preprocessing required
                    { Model.Inception_V2,      null }, // ????
                    { Model.ShuffleNet_V1,     () => TensorizationModels.Normalize0_1ThenZScore(224, 224, 4,
                                                                                                new float[] { 0.485f, 0.456f, 0.406f },
                                                                                                new float[] { 0.229f, 0.224f, 0.225f}) },
                    { Model.ShuffleNet_V2,     () => TensorizationModels.Normalize0_1ThenZScore(224, 224, 4,
                                                                                                new float[] { 0.485f, 0.456f, 0.406f },
                                                                                                new float[] { 0.229f, 0.224f, 0.225f}) },
                    { Model.EfficientNetLite4, () => TensorizationModels.NormalizeMinusOneToOneThenTransposeNHWC() },
                    // Large Models
                    { Model.AlexNet,           null }, // No preprocessing required
                    { Model.CaffeNet,          null }, // No preprocessing required
                    { Model.RCNN_ILSVRC13,     null }, // No preprocessing required
                    { Model.ResNet,            () => TensorizationModels.Normalize0_1ThenZScore(224, 224, 4,
                                                                                                new float[] { 0.485f, 0.456f, 0.406f },
                                                                                                new float[] { 0.229f, 0.224f, 0.225f}) },
                    { Model.VGG,               () => TensorizationModels.Normalize0_1ThenZScore(224, 224, 4,
                                                                                                new float[] { 0.485f, 0.456f, 0.406f },
                                                                                                new float[] { 0.229f, 0.224f, 0.225f}) },
                    { Model.ZFNet512,          null }, // No preprocessing required
                };
            }

            InitializeWindowsMachineLearning(currentModel_);
        }

        private void InitializeWindowsMachineLearning(Model model)
        {
            if (currentModel_ != loadedModel_)
            {
                var modelPath = modelDictionary_[model];
                inferenceSession_ = CreateLearningModelSession(modelPath);

                var preProcessor = preProcessorDictionary_[model];
                if (preProcessor != null)
                {
                    preProcessingSession_ = CreateLearningModelSession(preProcessor());
                }
                else
                {
                    preProcessingSession_ = null;
                }

                var postProcessor = postProcessorDictionary_[model];
                if (postProcessor != null)
                {
                    postProcessingSession_ = CreateLearningModelSession(postProcessor());
                }
                else
                {
                    postProcessingSession_ = null;
                }

                if (currentModel_ == Model.RCNN_ILSVRC13)
                {
                    labels_ = ilsvrc2013Labels_;
                }
                else
                {
                    labels_ = imagenetLabels_;
                }

                loadedModel_ = currentModel_;
            }
        }

#pragma warning disable CA1416 // Validate platform compatibility
        private (IEnumerable<string>, IReadOnlyList<float>) Classify(SoftwareBitmap softwareBitmap)
        {
            var input = (object)VideoFrame.CreateWithSoftwareBitmap(softwareBitmap);

            // PreProcess
            object preProcessedOutput = input;
            if (preProcessingSession_ != null)
            {
                var preProcessedResults = Evaluate(preProcessingSession_, input);
                preProcessedOutput = preProcessedResults.Outputs.First().Value;
                var preProcessedOutputTF = preProcessedOutput as TensorFloat;
                var shape = preProcessedOutputTF.Shape;
                System.Diagnostics.Debug.WriteLine("shape = {0}, {1}, {2}, {3}", shape[0], shape[1], shape[2], shape[3]);
            }

            // Inference
            var inferenceResults = Evaluate(inferenceSession_, preProcessedOutput);
            var inferenceOutput = inferenceResults.Outputs.First().Value as TensorFloat;
            System.Diagnostics.Debug.Assert(inferenceOutput.Shape[0] == BatchSize);
            System.Diagnostics.Debug.Assert(inferenceOutput.Shape[1] == NumLabels);

            // PostProcess
            var postProcessedOutputs = Evaluate(postProcessingSession_, inferenceOutput);
            var topKValues = postProcessedOutputs.Outputs["TopKValues"] as TensorFloat;
            var topKIndices = postProcessedOutputs.Outputs["TopKIndices"] as TensorInt64Bit;

            // Return results
            var probabilities = topKValues.GetAsVectorView();
            var indices = topKIndices.GetAsVectorView();
            var labels = indices.Select((index) => labels_[index]);

            return (labels, probabilities);
        }

        private static LearningModelEvaluationResult Evaluate(LearningModelSession session, object input, TensorFloat output = null)
        {
            // Create the binding
            var binding = new LearningModelBinding(session);

            // Create an emoty output, that will keep the output resources on the GPU
            // It will be chained into a the post processing on the GPU as well
            output = output ?? TensorFloat.Create();

            // Bind inputs and outputs
            // For squeezenet these evaluate to "data", and "squeezenet0_flatten0_reshape0"
            string inputName = session.Model.InputFeatures[0].Name;
            string outputName = session.Model.OutputFeatures[0].Name;
            binding.Bind(inputName, input);
            binding.Bind(outputName, output);

            // Evaluate
            return session.Evaluate(binding, "");
        }

        private static LearningModelSession CreateLearningModelSession(string modelPath)
        {
            var model = CreateLearningModel(modelPath);
            var session =  CreateLearningModelSession(model);
            return session;
        }

        private static LearningModelSession CreateLearningModelSession(LearningModel model)
        {
            var device = new LearningModelDevice(LearningModelDeviceKind.DirectXHighPerformance);
            var options = new LearningModelSessionOptions() {
                CloseModelOnSessionCreation = true              // Close the model to prevent extra memory usage
            };
            var session = new LearningModelSession(model, device, options);
            return session;
        }

        private static LearningModel CreateLearningModel(string modelPath)
        {
            var uri = new Uri(modelPath);
            var file = StorageFile.GetFileFromApplicationUriAsync(uri).GetAwaiter().GetResult();
            return LearningModel.LoadFromStorageFileAsync(file).GetAwaiter().GetResult();
        }
#pragma warning restore CA1416 // Validate platform compatibility

        private static Dictionary<long, string> LoadLabels(string csvFile)
        {
            var file = StorageFile.GetFileFromApplicationUriAsync(new Uri(csvFile)).GetAwaiter().GetResult();
            var text = Windows.Storage.FileIO.ReadTextAsync(file).GetAwaiter().GetResult();
            var labels = new Dictionary<long, string>();
            var records = text.Split(Environment.NewLine);
            foreach (var record in records)
            {
                var fields = record.Split(",", 2);
                if (fields.Length == 2)
                {
                    var index = long.Parse(fields[0]);
                    labels[index] = fields[1];
                }
            }
            return labels;
        }

        private void OpenButton_Clicked(object sender, RoutedEventArgs e)
        {
            var file = PickFile();
            if (file != null)
            {
                currentImage_ = file;
                BasicGridView.SelectedItem = null;
                TryPerformInference();
            }
        }

        private void SampleInputsGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var gridView = sender as GridView;
            var thumbnail = gridView.SelectedItem as WinMLSamplesGallery.Controls.Thumbnail;
            if (thumbnail != null)
            {
                var image = thumbnail.ImageUri;
                currentImage_ = StorageFile.GetFileFromApplicationUriAsync(new Uri(image)).GetAwaiter().GetResult();
                TryPerformInference();
            }
        }

        private void AllModelsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var gridView = sender as GridView;
            var link = gridView.SelectedItem as WinMLSamplesGallery.Link;
            currentModel_ = link.Tag;
            TryPerformInference();
        }

        private void TryPerformInference()
        {
            if (currentImage_ != null)
            {
                if (AllModelsGrid.SelectedItem == null)
                {
                    AllModelsGrid.SelectRange(new Microsoft.UI.Xaml.Data.ItemIndexRange(0, 1));
                }
                else
                {
                    EnsureInit();

                    var softwareBitmap = CreateSoftwareBitmapFromStorageFile(currentImage_);
                    RenderImageInMainPanel(softwareBitmap);

                    var (labels, probabilities) = Classify(softwareBitmap);
                    RenderInferenceResults(labels, probabilities);

                }
            }
        }

        private void RenderInferenceResults(IEnumerable<string> labels, IReadOnlyList<float> probabilities)
        {
            var indices = Enumerable.Range(1, probabilities.Count);
            var zippedResults = indices.Zip(labels.Zip(probabilities));
            var results = zippedResults.Select(
                (zippedResult) =>
                    new Controls.Prediction {
                        Index = zippedResult.First,
                        Name = zippedResult.Second.First.Trim(new char[] { ',' }),
                        Probability = zippedResult.Second.Second.ToString("E4")
                    });
            InferenceResults.ItemsSource = results;
            InferenceResults.SelectedIndex = 0;
        }

        private void RenderImageInMainPanel(SoftwareBitmap softwareBitmap)
        {
            SoftwareBitmap displayBitmap = softwareBitmap;
            //Image control only accepts BGRA8 encoding and Premultiplied/no alpha channel. This checks and converts
            //the SoftwareBitmap we want to bind.
            if (displayBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
                displayBitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
            {
                displayBitmap = SoftwareBitmap.Convert(displayBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            }

            // get software bitmap souce
            var source = new SoftwareBitmapSource();
            source.SetBitmapAsync(displayBitmap).GetAwaiter();
            // draw the input image
            InputImage.Source = source;
        }

        private StorageFile PickFile()
        {
            FileOpenPicker openPicker = new FileOpenPicker();
            openPicker.ViewMode = PickerViewMode.Thumbnail;
            openPicker.FileTypeFilter.Add(".jpg");
            openPicker.FileTypeFilter.Add(".bmp");
            openPicker.FileTypeFilter.Add(".png");
            openPicker.FileTypeFilter.Add(".jpeg");

            // When running on win32, FileOpenPicker needs to know the top-level hwnd via IInitializeWithWindow::Initialize.
            if (Window.Current == null)
            {
                var picker_unknown = Marshal.GetComInterfaceForObject(openPicker, typeof(IInitializeWithWindow));
                var initializeWithWindowWrapper = (IInitializeWithWindow)Marshal.GetTypedObjectForIUnknown(picker_unknown, typeof(IInitializeWithWindow));
                IntPtr hwnd = GetActiveWindow();
                initializeWithWindowWrapper.Initialize(hwnd);
            }

            return openPicker.PickSingleFileAsync().GetAwaiter().GetResult();
        }

        private static SoftwareBitmap CreateSoftwareBitmapFromStorageFile(StorageFile file)
        {
            var stream = file.OpenAsync(FileAccessMode.Read).GetAwaiter().GetResult();
            var decoder = BitmapDecoder.CreateAsync(stream).GetAwaiter().GetResult();
            return decoder.GetSoftwareBitmapAsync().GetAwaiter().GetResult();
        }
    }
}
