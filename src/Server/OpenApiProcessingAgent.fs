module Orn.Registry.OpenApiProcessing

open Orn.Registry.JsonLdParsing
open Orn.Registry.BasicTypes
open DouglasConnect.Http
open Cvdm.ErrorHandling
open Orn.Registry.OpenApiTransformer
open System.Threading
open Orn.Registry.Shared

type Message =
  | AddToIndex of OpenApiUrl
  | RemoveFromIndex of OpenApiUrl
  | ReindexFailed

type OpenRiskNetServiceInfo =
  { TripleStore : VDS.RDF.TripleStore
    OpenApiServiceInformation : OpenApiServiceInformation
  }

type TripleIndexingStatus =
  | InProgress
  | Indexed of OpenRiskNetServiceInfo
  | Failed of string

let updateMap key newval map =
  let mutable found = false
  let newMap =
    map
    |> Map.map (fun k v ->
      if k = key then
        found <- true
        newval
      else
        v)
  if found then
    Some newMap
  else
    None


type OpenApiAgent(feedbackAgent : Orn.Registry.Feedback.FeedbackAgent, cancelToken : CancellationToken) =
  let mutable serviceMap : Map<OpenApiUrl,TripleIndexingStatus> = Map []

  let rec agentFunction ((feedbackAgent : Orn.Registry.Feedback.FeedbackAgent)) (agent : Agent<Message>) =
    async {

      let! message = agent.Receive()
      let (>>=) a b = Result.bind b a
      let makeTuple a b = (a,b)
      match message with
      | AddToIndex (OpenApiUrl url) ->
          serviceMap <- serviceMap |> Map.add (OpenApiUrl url) InProgress
          try
            let! result =
              asyncResult {
                printfn "Downloading openapi definition for %s" url
                let headers = [ FSharp.Data.HttpRequestHeaders.Accept FSharp.Data.HttpContentTypes.Json ]
                let! openapistring =
                  SafeAsyncHttp.AsyncHttpTextResult(url, timeout=System.TimeSpan.FromSeconds(20.0), headers=headers)
                  |> AsyncResult.mapError (fun err -> err.ToString())
                  |> AsyncResult.teeError (fun _ -> feedbackAgent.Post(Orn.Registry.Shared.OpenApiDownloadFailed(OpenApiUrl url)))
                printfn "Downloading worked, processing..."

                let! description, openapi =
                  openapistring
                  |> OpenApiRaw
                  |> TransformOpenApiToV3Dereferenced (OpenApiUrl url)
                  |> Result.teeError (fun err -> feedbackAgent.Post(Orn.Registry.Shared.OpenApiParsingFailed(OpenApiUrl url, err)))
                let! jsonld =
                  fixOrnJsonLdContext openapi
                  |> Result.teeError (fun err -> feedbackAgent.Post(Orn.Registry.Shared.JsonLdParsingError(OpenApiUrl url, err)))
                let! tripleStore =
                  loadJsonLdIntoTripleStore jsonld
                  |> Result.teeError (fun err -> feedbackAgent.Post(Orn.Registry.Shared.JsonLdParsingError(OpenApiUrl url, err)))
                return (description, tripleStore)
              }

            let updatedMap =
              match result with
              | Ok (serviceInformation, tripleStore) ->
                  printfn "Loading json-ld into triple store worked for service %s" url
                  serviceMap |> updateMap (OpenApiUrl url) (Indexed {TripleStore = tripleStore; OpenApiServiceInformation = serviceInformation})
              | Error msg ->
                  printfn "Loading json-ld into triple store FAILED for service %s" url
                  serviceMap |> updateMap (OpenApiUrl url) (Failed msg)

            match updatedMap with
            | Some map ->
                serviceMap <- map
            | None ->
                printfn "Update of map failed: %s" url
          with
          | :? System.Net.WebException ->
            printfn "Timeout occured in OpenApi processing agent when processing: %s" url
            let updatedMap = (serviceMap |> updateMap (OpenApiUrl url) (Failed "Timeout while trying to download swagger definition"))
            match updatedMap with
            | Some updated ->
                serviceMap <- updated
            | _ -> ()
          | ex ->
            feedbackAgent.Post(Orn.Registry.Shared.JsonLdParsingError(OpenApiUrl url, ex.ToString()))
            printfn "Exception occured in OpenApi processing agent: %O" ex

      | RemoveFromIndex url ->
          serviceMap <- serviceMap |> Map.remove url

      | ReindexFailed ->
          serviceMap <-
            Map.fold (fun state key value ->
            match value with
             | InProgress -> Map.add key value state
             | Indexed _ -> Map.add key value state
             | Failed _ ->
                agent.Post(AddToIndex(key))
                state // drop failed services after queing them for reindexing
            ) Map.empty serviceMap

      do! agentFunction feedbackAgent agent
    }

  let agent = Agent.Start(agentFunction feedbackAgent, cancelToken)

  member this.ServiceMap = serviceMap

  member this.SendMessage (msg : Message) = agent.Post msg
