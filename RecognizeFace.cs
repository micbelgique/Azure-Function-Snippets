#r "Newtonsoft.Json"

using System.Net;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using System.Text;

public static TraceWriter logs;

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log)
{
    logs = log; // Just for logging purpose
    var picture = await req.Content.ReadAsStreamAsync();

    // We send the picture to our detection function
    string answer = await LaunchDetection(picture);

    // If we have no answer for a reason, we will send an Bad Request Http Status Code (aka 400)
    if (String.IsNullOrEmpty(answer))
    {
        return req.CreateResponse(HttpStatusCode.BadRequest);
    }

    // If everything seems good we will send a 200 Http Status and the Json
    // I consider that it's ok if we have analyzed the picture and there is no faces in it or we didn't recognized the faces
    var response = new HttpResponseMessage()
    {
        Content = new StringContent(answer),
        StatusCode = HttpStatusCode.OK,
    };
    response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

    return response;
}


static async Task<string> LaunchDetection(Stream image)
{
    var personsIdentified = new List<Person>();

    // First step : detecting faces
    var facesDetected = await DetectFaces(image);
    if (facesDetected.Count() > 0)
    {
        // We will try to recognize each faces in the picture
        foreach (var face in facesDetected)
        {
            // We try to identify to whom the face belong
            var faceIdentifyResult = await IdentifyFace(face.faceId);
            if (faceIdentifyResult.Count() > 0)
            {
                if (faceIdentifyResult[0].candidates.Count() > 0)
                {
                    // We assume that the 1st candidate returned is the good one
                    var person = await IdentifyPerson(faceIdentifyResult[0].candidates[0].personId);

                    // Add the person to the persons list
                    personsIdentified.Add(new Person { Name = person.name });
                }
            }
            else
            {
                // If no face is *recognized* in the picture we send an error message
                return "{\"Error\" : \"No face recognized.\"}";
            }
        }
    }
    else
    {
        // If no face is *detected* in the picture we send an error message
        return "{\"Error\" : \"No face detected.\"}";
    }

    if (personsIdentified.Count() == 0)
    {
        // If we've found faces in the picture but don't know the persons
        return "{\"Error\" : \"Only unknow person identified.\"}";
    }

    return JsonConvert.SerializeObject(personsIdentified);
}


// We send our picture as a stream to the MS Cognitive Face APIs and ask it to return us the informations about the face
static async Task<List<DetectedFacesDTO>> DetectFaces(Stream image)
{
    var faceDetectedResult = new List<DetectedFacesDTO>();
    using (var client = new HttpClient())
    {

        logs.Info("Face detection has started ...");

        var content = new StreamContent(image);
        var url = "https://api.projectoxford.ai/face/v1.0/detect?returnFaceId=true&returnFaceLandmarks=false";
        client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", Environment.GetEnvironmentVariable("FACE_API_KEY"));

        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var httpResponse = await client.PostAsync(url, content);

        if (httpResponse.StatusCode == HttpStatusCode.OK)
        {
            logs.Info("- Face(s) have been detected ...");

            string faceDetectedResultJson = await httpResponse.Content.ReadAsStringAsync();
            faceDetectedResult = JsonConvert.DeserializeObject<List<DetectedFacesDTO>>(faceDetectedResultJson);

            logs.Info("- Number of face(s) detected : " + faceDetectedResult.Count());

        }
        else
        {
            logs.Info("No face(s) have been detected ...");
        }

        logs.Info("Face detection has finished ...");
    }
    return faceDetectedResult;
}

// We send the data about the face and ask it to return us to whom the face belong
static async Task<List<IdentifiedFaceDTO>> IdentifyFace(string faceId)
{
    var faceIdentifyResult = new List<IdentifiedFaceDTO>();

    using (var client = new HttpClient())
    {

        logs.Info("Face Identification has started ...");
        var infos = new FaceIdentityRequestDTO()
        {
            personGroupId = Environment.GetEnvironmentVariable("PERSON_GROUP_ID"),
            faceIds = new String[] { faceId },
            maxNumOfCandidatesReturned = 1,
            confidenceThreshold = 0.5f,
        };

        var url = "https://api.projectoxford.ai/face/v1.0/identify";
        client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", Environment.GetEnvironmentVariable("FACE_API_KEY"));

        var jsonObject = JsonConvert.SerializeObject(infos);
        var content = new StringContent(jsonObject.ToString(), Encoding.UTF8, "application/json");


        var httpResponse = await client.PostAsync(url, content);


        if (httpResponse.StatusCode == HttpStatusCode.OK)
        {
            logs.Info("- Face(s) have been identified ...");

            string faceIdentifyResultJson = await httpResponse.Content.ReadAsStringAsync();
            faceIdentifyResult = JsonConvert.DeserializeObject<List<IdentifiedFaceDTO>>(faceIdentifyResultJson);
        }
        else
        {
            logs.Info("- No face(s) have been identified ...");
        }

        logs.Info("Face identification has finished ...");
    }
    return faceIdentifyResult;
}

// We send the data about the person and get some information (faces list, name, etc.) in return
static async Task<IdentifiedPersonDTO> IdentifyPerson(string personId)
{
    var getPersonResult = new IdentifiedPersonDTO();
    using (var client = new HttpClient())
    {

        logs.Info("People information retrieving has started ...");

        var personGroupId = Environment.GetEnvironmentVariable("PERSON_GROUP_ID");
        var url = $"https://api.projectoxford.ai/face/v1.0/persongroups/{personGroupId}/persons/{personId}";
        client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", Environment.GetEnvironmentVariable("FACE_API_KEY"));

        var httpResponse = await client.GetAsync(url);


        if (httpResponse.StatusCode == HttpStatusCode.OK)
        {
            logs.Info("- People information have been retrieved ...");

            string getPersonResultJson = await httpResponse.Content.ReadAsStringAsync();
            getPersonResult = JsonConvert.DeserializeObject<IdentifiedPersonDTO>(getPersonResultJson);

            logs.Info("- People name : " + getPersonResult.name);
        }
        else
        {
            logs.Info("- No people information have been retrieved ...");
        }
        logs.Info("People information retrieving has finished ...");
    }
    return getPersonResult;
}

// Bellow, some useful DTO

public class Person
{
    [JsonProperty("Name")]
    public string Name { get; set; }
}

public class DetectedFacesDTO
{
    [JsonProperty("faceId")]
    public string faceId { get; set; }

    [JsonProperty("faceRectangle")]
    public FaceRectangle faceRectangle { get; set; }

    public class FaceRectangle
    {
        [JsonProperty("top")]
        public int top { get; set; }

        [JsonProperty("left")]
        public int left { get; set; }

        [JsonProperty("width")]
        public int width { get; set; }

        [JsonProperty("height")]
        public int height { get; set; }
    }
}

public class IdentifiedFaceDTO
{
    [JsonProperty("faceId")]
    public string faceId { get; set; }

    [JsonProperty("candidates")]
    public Candidate[] candidates { get; set; }

    public class Candidate
    {
        [JsonProperty("personId")]
        public string personId { get; set; }

        [JsonProperty("confidence")]
        public float confidence { get; set; }
    }
}

public class FaceIdentityRequestDTO
{
    public string personGroupId { get; set; }
    public string[] faceIds { get; set; }
    public int maxNumOfCandidatesReturned { get; set; }
    public float confidenceThreshold { get; set; }
}

public class IdentifiedPersonDTO
{
    [JsonProperty("personId")]
    public string personId { get; set; }

    [JsonProperty("persistedFaceIds")]
    public string[] persistedFaceIds { get; set; }

    [JsonProperty("name")]
    public string name { get; set; }

    [JsonProperty("userData")]
    public object userData { get; set; }
}
