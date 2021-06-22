// Written by Eric Staykov (2021) for Ethan Scott's Lab at The University of Queensland 
// Leandro Aluisio Scholz wrote the project specifications and helped test the code

using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Collections;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing.Design;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using xiApi.NET;
using OpenCV.Net;
using Bonsai;

namespace XimeaCamera
{

    [Description("Generates a sequence of images acquired from the specified Ximea camera.")]
    public class XimeaCameraCapture : Source<IplImage>
    {
        readonly object captureLock = new object();
        IObservable<IplImage> source;
        IplImage output;
        xiCam myCam;
        int[] resolutionsX = { 1920, 1600, 1280, 1280, 1024, 1024 };
        int[] resolutionsY = { 1080, 900, 960, 720, 768, 576 };
        int fullResolutionWidth = 1920;
        int fullResolutionHeight = 1080;
        float gain = 12; // default value
        const float gainMin = 0;
        const float gainMax = 18;
        int deviceId = 0; // default value
        const int deviceIdMin = 0;
        const int deviceIdMax = 10;
        float exposureTimeCoarseMS = 2; // default value
        const float exposureTimeCoarseMSMin = 0;
        const float exposureTimeCoarseMSMax = 999;
        int exposureTimeFineUS = 1; // default value
        const int exposureTimeFineUSMin = 1;
        const int exposureTimeFineUSMax = 1000;
        int exposureTimeTotalUS;
        int frameRate = 500; // default value
        const int frameRateMin = 1;
        const int frameRateMax = 2263;
        int resolution = 0; // default value
        const int resolutionMin = 0;
        const int resolutionMax = 5;
        int channelSelector = 1; // default value
        const int channelSelectorMin = 1;
        const int channelSelectorMax = 3;
        int lensControl = 0; // default value
        const int lensControlMin = 0;
        const int lensControlMax = 1;
        int imageStabilisation = 0; // default value
        const int imageStabilisationMin = 0;
        const int imageStabilisationMax = 1;
        int apertureStepSize = 1;
        float apertureValue = 4; // default value
        const float apertureValueMin = (float)1.4;
        const float apertureValueMax = 22;
        int focusStepSize = 0; // default value
        const int focusStepSizeMin = -100;
        const int focusStepSizeMax = 100;
        int focusValue = 0;
        bool cameraConnected = false;

        public void setImageStabilisation()
        {
            setCameraParameter(PRM.LENS_FEATURE_SELECTOR, LENS_FEATURE_SELECTOR.IMAGE_STABILIZATION_ENABLED);
            setCameraParameter(PRM.LENS_FEATURE, imageStabilisation);
        }

        public void setFocus()
        {
            setCameraParameter(PRM.LENS_FOCUS_MOVEMENT_VALUE, focusStepSize);
            setCameraParameter(PRM.LENS_FOCUS_MOVE, focusValue);
        }

        public void setAperture()
        {
            setCameraParameter(PRM.LENS_APERTURE_INDEX, apertureStepSize);
            setCameraParameter(PRM.LENS_APERTURE_VALUE, apertureValue);
        }

        public void calculateApplyOffsetResolution(int width, int height)
        {
            int offsetX = (fullResolutionWidth / 2) - (width / 2);
            int offsetY = (fullResolutionHeight / 2) - (height / 2);
            setCameraParameter(PRM.WIDTH, width);
            setCameraParameter(PRM.HEIGHT, height);
            setCameraParameter(PRM.OFFSET_X, offsetX);
            setCameraParameter(PRM.OFFSET_Y, offsetY);
        }

        public void setResolution()
        {
            resolution = clampValue(resolution, resolutionMin, resolutionMax);
            calculateApplyOffsetResolution(resolutionsX[resolution], resolutionsY[resolution]);
        }

        public void calculateSetExposureTimeTotal()
        {
            exposureTimeTotalUS = ((int)(exposureTimeCoarseMS * 1000)) + exposureTimeFineUS;
            setCameraParameter(PRM.EXPOSURE, exposureTimeTotalUS);
        }

        public int clampValue(int value, int min, int max)
        {
            return (value < min) ? min : (value > max) ? max : value;
        }

        public float clampValue(float value, float min, float max)
        {
            return (value < min) ? min : (value > max) ? max : value;
        }

        public bool setParameterChecker(string parameter)
        {
            // clamp parameter values
            deviceId = clampValue(deviceId, deviceIdMin, deviceIdMax);
            exposureTimeCoarseMS = clampValue(exposureTimeCoarseMS, exposureTimeCoarseMSMin, exposureTimeCoarseMSMax);
            exposureTimeFineUS = clampValue(exposureTimeFineUS, exposureTimeFineUSMin, exposureTimeFineUSMax);
            frameRate = clampValue(frameRate, frameRateMin, frameRateMax);
            gain = clampValue(gain, gainMin, gainMax);
            resolution = clampValue(resolution, resolutionMin, resolutionMax);
            channelSelector = clampValue(channelSelector, channelSelectorMin, channelSelectorMax);
            lensControl = clampValue(lensControl, lensControlMin, lensControlMax);
            imageStabilisation = clampValue(imageStabilisation, imageStabilisationMin, imageStabilisationMax);
            apertureValue = clampValue(apertureValue, apertureValueMin, apertureValueMax);
            focusStepSize = clampValue(focusStepSize, focusStepSizeMin, focusStepSizeMax);
            // no parameter to set
            if (parameter.Equals("NONE"))
            {
                return false;
            }
            // don't set parameters until connected to camera
            if (!cameraConnected)
            {
                return false;
            }
            // don't set lens control parameters if lens control isn't enabled
            if ((lensControl == 0) && (parameter.Equals(PRM.LENS_FEATURE_SELECTOR) | parameter.Equals(PRM.LENS_FEATURE) | parameter.Equals(PRM.LENS_APERTURE_INDEX) | parameter.Equals(PRM.LENS_APERTURE_VALUE) | parameter.Equals(PRM.LENS_FOCUS_MOVEMENT_VALUE) | parameter.Equals(PRM.LENS_FOCUS_MOVE)))
            {
                return false;
            }
            return true;
        }

        public void setCameraParameter(string parameter, int value)
        {
            if (setParameterChecker(parameter))
            {
                myCam.SetParam(parameter, value);
            }
        }

        public void setCameraParameter(string parameter, float value)
        {
            if (setParameterChecker(parameter))
            {
                myCam.SetParam(parameter, value);
            }
        }

        public XimeaCameraCapture()
        {
            source = Observable.Create<IplImage>((observer, cancellationToken) =>
            {
                return Task.Factory.StartNew(() =>
                {
                    lock (captureLock)
                    {
                        myCam = new xiCam();
                        try
                        {
                            // Get number of connected devicess
                            int numDevices = 0;
                            myCam.GetNumberDevices(out numDevices);

                            if (0 == numDevices)
                            {
                                throw new System.ApplicationException("No devices found");
                            }
                            else
                            {
                                Console.WriteLine("Found {0} connected devices.", numDevices);
                            }

                            // Initialise camera
                            myCam.OpenDevice(deviceId);

                            cameraConnected = true;

                            // Get device model name
                            string strVal;
                            myCam.GetParam(PRM.DEVICE_NAME, out strVal);
                            Console.WriteLine("Found device {0}.", strVal);

                            // Get device type
                            myCam.GetParam(PRM.DEVICE_TYPE, out strVal);
                            Console.WriteLine("Device type {0}.", strVal);

                            // Get device serial number
                            myCam.GetParam(PRM.DEVICE_SN, out strVal);
                            Console.WriteLine("Device serial number {0}", strVal);

                            // Set acquisition mode to fixed frame rate
                            setCameraParameter(PRM.ACQ_TIMING_MODE, ACQ_TIMING_MODE.FRAME_RATE_LIMIT);
                            Console.WriteLine("Set acquisition mode to frame rate limited");

                            // Set device frame rate
                            setCameraParameter(PRM.FRAMERATE, frameRate);
                            Console.WriteLine("Frame rate was limited to {0} FPS", frameRate);

                            // Set image resolution
                            setResolution();
                            Console.WriteLine("Image resolution was set to option {0} (see descriptions)", resolution);

                            // Set device exposure
                            calculateSetExposureTimeTotal();
                            Console.WriteLine("Exposure was set to {0} milliseconds", exposureTimeTotalUS / 1000);

                            // Set device gain
                            setCameraParameter(PRM.GAIN, gain);
                            Console.WriteLine("Gain was set to {0} dB", gain);

                            // Set image output format to monochrome 8 bit
                            setCameraParameter(PRM.IMAGE_DATA_FORMAT, IMG_FORMAT.MONO8);
                            Console.WriteLine("Image format was set to 8 bit monochrome and {0} channel", channelSelector);

                            // Set lens control
                            setCameraParameter(PRM.LENS_MODE, lensControl);
                            Console.WriteLine("Lens control was set to {0}", lensControl);

                            if (lensControl == 1)
                            {
                                // Set image stabilisation 
                                setImageStabilisation();
                                Console.WriteLine("Image stabilisation was set to {0}", imageStabilisation);
                                // Set aperture control
                                setAperture();
                                Console.WriteLine("Aperture value set to {0}", apertureValue);
                            }

                            // UNSAFE buffer policy
                            setCameraParameter(PRM.BUFFER_POLICY, BUFF_POLICY.UNSAFE);

                            // Start acquisition
                            myCam.StartAcquisition();

                            Bitmap myImage;
                            System.Drawing.Imaging.BitmapData imgData;
                            Rectangle rect;
                            IplImage tempImage;
                            IntPtr ptr;
                            int timeout = 10000;

                            // Capture images continuously
                            while (!cancellationToken.IsCancellationRequested)
                            {
                                unsafe // Allow pointers
                                {
                                    myCam.GetImage(out myImage, timeout);
                                    rect = new Rectangle(0, 0, myImage.Width, myImage.Height);
                                    imgData = myImage.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite, myImage.PixelFormat);
                                    ptr = imgData.Scan0;
                                    tempImage = new IplImage(new OpenCV.Net.Size(myImage.Width, myImage.Height), IplDepth.U8, 1, ptr);
                                    if (channelSelector == 3)
                                    {
                                        output = new IplImage(tempImage.Size, tempImage.Depth, 3);
                                        // single channel to 3 channel
                                        CV.CvtColor(tempImage, output, ColorConversion.Gray2Rgb);
                                    }
                                    else
                                    {
                                        output = new IplImage(tempImage.Size, tempImage.Depth, 1);
                                        // single channel to single channel
                                        CV.Copy(tempImage, output);
                                    }
                                    myImage.UnlockBits(imgData);
                                    observer.OnNext(output.Clone());
                                }
                            }

                            // Stop acquisition
                            myCam.StopAcquisition();
                        }

                        catch (System.ApplicationException appExc)
                        {
                            // Show handled error
                            Console.WriteLine(appExc.Message);
                            // Prevent console from closing automatically
                            System.Console.ReadLine();
                        }

                        finally
                        {
                            myCam.CloseDevice();
                            cameraConnected = false;
                        }
                    }
                },
                cancellationToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            })
            .PublishReconnectable()
            .RefCount();
        }
        public override IObservable<IplImage> Generate()
        {
            return source;
        }

        [Range(deviceIdMin, deviceIdMax)]
        [Description("The ID of the device to open. Range: 0 to 10. Example: 0 is the first camera, 1 is the second, etc.")]
        [Editor(DesignTypes.SliderEditor, "System.Drawing.Design.UITypeEditor, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")]
        public int A_Device_Id
        {
            get { return deviceId; }
            set
            {
                deviceId = value;
                setCameraParameter("NONE", deviceId);
            }
        }

        [Range(exposureTimeCoarseMSMin, exposureTimeCoarseMSMax)]
        [Description("Coarse control of exposure time in milliseconds (ms). Total exposure = coarse + fine. Range: 0 to 999. Example: 2.")]
        [Editor(DesignTypes.SliderEditor, "System.Drawing.Design.UITypeEditor, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")]
        public float B_Exposure_Time_Coarse
        {
            get { return exposureTimeCoarseMS; }
            set
            {
                exposureTimeCoarseMS = value;
                calculateSetExposureTimeTotal();
            }
        }

        [Range(exposureTimeFineUSMin, exposureTimeFineUSMax)]
        [Description("Fine control of exposure time in microseconds (us). Total exposure = coarse + fine. Range: 1 to 1000. Example: 1.")]
        [Editor(DesignTypes.SliderEditor, "System.Drawing.Design.UITypeEditor, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")]
        public int C_Exposure_Time_Fine
        {
            get { return exposureTimeFineUS; }
            set
            {
                exposureTimeFineUS = value;
                calculateSetExposureTimeTotal();
            }
        }

        [Range(frameRateMin, frameRateMax)]
        [Description("The frame rate for the camera in Hertz (Hz). Range: 1 to 2263. Example: 500.")]
        [Editor(DesignTypes.SliderEditor, "System.Drawing.Design.UITypeEditor, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")]
        public int D_Frame_Rate
        {
            get { return frameRate; }
            set
            {
                frameRate = value;
                setCameraParameter(PRM.FRAMERATE, frameRate);
            }
        }

        [Range(gainMin, gainMax)]
        [Description("The gain for the camera in decibels (dB). Range: 0 to 18.00. Example: 12.00.")]
        [Editor(DesignTypes.SliderEditor, "System.Drawing.Design.UITypeEditor, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")]
        public float E_Gain
        {
            get { return gain; }
            set
            {
                gain = value;
                setCameraParameter(PRM.GAIN, gain);
            }
        }

        [Range(resolutionMin, resolutionMax)]
        [Description("The resolution of the image. Cannot be set while camera is recording/live. 1920x1080 (0), 1600x900 (1), 1280x960 (2), 1280x720 (3), 1024x768 (4), 1024x576 (5). Use Crop node to make a custom ROI.")]
        [Editor(DesignTypes.SliderEditor, "System.Drawing.Design.UITypeEditor, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")]
        public int F_Resolution
        {
            get { return resolution; }
            set
            {
                resolution = value;
                setResolution();
            }
        }

        [Range(channelSelectorMin, channelSelectorMax)]
        [Description("Select between single channel (1) and three channel RGB (3) image output.")]
        [Editor(DesignTypes.SliderEditor, "System.Drawing.Design.UITypeEditor, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")]
        public int G_Channel_Selector
        {
            get { return channelSelector; }
            set
            {
                channelSelector = value;
                setCameraParameter("NONE", channelSelector);
            }
        }

        [Range(lensControlMin, lensControlMax)]
        [Description("Enable (1) or disable (0) lens control settings (image stabilisation and aperture and focus control).")]
        [Editor(DesignTypes.SliderEditor, "System.Drawing.Design.UITypeEditor, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")]
        public int H_Lens_Control
        {
            get { return lensControl; }
            set
            {
                lensControl = value;
                setCameraParameter(PRM.LENS_MODE, lensControl);
                if (lensControl == 1)
                {
                    setImageStabilisation();
                    setAperture();
                }
            }
        }

        [Range(imageStabilisationMin, imageStabilisationMax)]
        [Description("Enable (1) or disable (0) image stabilisation.")]
        [Editor(DesignTypes.SliderEditor, "System.Drawing.Design.UITypeEditor, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")]
        public int I_Image_Stabilisation
        {
            get { return imageStabilisation; }
            set
            {
                imageStabilisation = value;
                setImageStabilisation();
            }
        }

        [Range(apertureValueMin, apertureValueMax)]
        [Description("Aperture control to closest possible value. Range: 1.4 to 22. Examples: 1.4, 2, 2.8, 4, 5.6, 8, 11, 16, 22.")]
        [Editor(DesignTypes.SliderEditor, "System.Drawing.Design.UITypeEditor, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")]
        public float J_Aperture_Control
        {
            get { return apertureValue; }
            set
            {
                apertureValue = value;
                setAperture();
            }
        }

        [Range(focusStepSizeMin, focusStepSizeMax)]
        [Description("Focus control. Range: -100 to 100. Example: 0 for no change and 100 for maximum change.")]
        [Editor(DesignTypes.SliderEditor, "System.Drawing.Design.UITypeEditor, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")]
        public int K_Focus_Control
        {
            get { return focusStepSize; }
            set
            {
                focusStepSize = value;
                setFocus();
            }
        }

    }
}