namespace Jernl

module BsonDocConverter =
    open MongoDB.Bson
    open Jernl.Models

    let serializeBsonTo<'T> (doc: BsonDocument) =
        Serialization.BsonSerializer.Deserialize<'T>(doc)
    
    let toArticleList docs =
        match docs with
        | Some bdoc -> List.map serializeBsonTo<Article> bdoc |> List.toArray
        | None -> [||]
    
    let toUserDetail (bdoc: BsonDocument) = {
        username = bdoc.["username"].AsString
        email = bdoc.["email"].AsString
        token = bdoc.["passwordhash"].AsString
        bio = bdoc.["bio"].AsString
        image = bdoc.["image"].AsString
    }

    let toProfileDetail (bdoc: BsonDocument) = {
        username = bdoc.["username"].AsString
        bio = bdoc.["bio"].AsString
        image = bdoc.["image"].AsString
        following = false
    }

    let toUser (bdoc: BsonDocument) =
        { user = (toUserDetail (bdoc.["user"].AsBsonDocument)) }

    let toProfile (bdoc: BsonDocument) =
        { profile = (toProfileDetail (bdoc.["user"].AsBsonDocument)) }
    
    let toHash (bdoc: BsonDocument) =
        let currentUser = Seq.find (fun (user: BsonElement) -> user.Name = "user") bdoc
        currentUser.Value.AsBsonDocument.["passwordhash"].AsString
    
    let extractPasswordHash bdoc =
        let userToLogin = Seq.find (fun (user: BsonElement) -> user.Name = "user") bdoc
        userToLogin.Value.AsBsonDocument.["passwordhash"].AsString
    
    let toUserId (bdoc: BsonDocument) =
        bdoc.["_id"].AsString