﻿namespace CardManagement.Data

module EntityToDomainMapping =
    open CardManagement
    open CardDomain
    open CardDomainEntities
    open Common.Errors
    open FsToolkit.ErrorHandling
    open CardManagement.Common.CommonTypes
    open CardManagement.Common

    let inline private mapValidationError (entity: ^a) (err: ValidationError)
        : InvalidDbDataError =
        { EntityId = entityId entity
          EntityName = typeof< ^a>.Name
          Message = sprintf "Could not deserialize field %s. Message: %s" err.FieldPath err.Message }

    let private validateCardEntityWithAccInfo (cardEntity: CardEntity, cardAccountEntity)
        : Result<Card * AccountInfo, ValidationError> =
        result {
            let! cardNumber = CardNumber.create "cardNumber" cardEntity.CardNumber
            let! name = LetterString.create "name" cardEntity.Name
            let! month = Month.create "expirationMonth" cardEntity.ExpirationMonth
            let! year = Year.create "expirationYear" cardEntity.ExpirationYear
            let accountInfo =
                { Balance = Money cardAccountEntity.Balance
                  DailyLimit = DailyLimit.ofDecimal cardAccountEntity.DailyLimit
                  Holder = cardEntity.UserId }
            let cardAccountInfo =
                if cardEntity.IsActive then
                    accountInfo
                    |> Active
                else Deactivated
            return
                ({ Number = cardNumber
                   Name = name
                   HolderId = cardEntity.UserId
                   Expiration = (month, year)
                   AccountDetails = cardAccountInfo }, accountInfo)
        }

    let private validateCardEntity (cardEntity: CardEntity, cardAccountEntity) : Result<Card, ValidationError> =
        validateCardEntityWithAccInfo (cardEntity, cardAccountEntity)
        |> Result.map fst

    let mapCardEntity (cardEntity, cardAccountEntity) =
        validateCardEntity (cardEntity, cardAccountEntity)
        |> Result.mapError (mapValidationError cardEntity)

    let mapCardEntityWithAccountInfo (cardEntity, cardAccountEntity) =
        validateCardEntityWithAccInfo (cardEntity, cardAccountEntity)
        |> Result.mapError (mapValidationError cardEntity)

    let private validateAddressEntity (entity: AddressEntity) : Result<Address, ValidationError> =
        result {
            let! country = parseCountry entity.Country
            let! city = LetterString.create "city" entity.City
            let! postalCode = PostalCode.create "postalCode" entity.PostalCode
            return
                { Country = country
                  City = city
                  PostalCode = postalCode
                  AddressLine1 = entity.AddressLine1
                  AddressLine2 = entity.AddressLine2 }
        }

    let mapAddressEntity entity : Result<Address, InvalidDbDataError>=
        validateAddressEntity entity
        |> Result.mapError (mapValidationError entity)

    let private validateUserInfoEntity (entity: UserEntity) : Result<UserInfo, ValidationError> =
        result {
            let! name = LetterString.create "name" entity.Name
            let! address = validateAddressEntity entity.Address
            return
                { Id = entity.UserId
                  Name = name
                  Address = address}
        }

    let mapUserInfoEntity (entity: UserEntity) : Result<UserInfo, InvalidDbDataError> =
        validateUserInfoEntity entity
        |> Result.mapError (mapValidationError entity)

    let mapUserEntity (entity: UserEntity) (cardEntities: (CardEntity * CardAccountInfoEntity) list)
        : Result<User, InvalidDbDataError> =
        result {
            let! userInfo = validateUserInfoEntity entity
            let! cards = List.map validateCardEntity cardEntities |> Result.combine

            return
                { UserInfo = userInfo
                  Cards = cards |> Set.ofList }
        } |> Result.mapError (mapValidationError entity)

    let mapBalanceOperationEntity (entity: BalanceOperationEntity) : Result<BalanceOperation, InvalidDbDataError> =
        result {
            let! cardNumber = entity.Id.CardNumber |> CardNumber.create "id.cardNumber"
            let! balanceChange =
                if entity.BalanceChange < 0M then
                    -entity.BalanceChange |> MoneyTransaction.create |> Result.map Decrease
                else entity.BalanceChange |> MoneyTransaction.create |> Result.map Increase
            return
                { CardNumber = cardNumber
                  NewBalance = Money entity.NewBalance
                  Timestamp = entity.Id.Timestamp
                  BalanceChange = balanceChange }
        } |> Result.mapError (mapValidationError entity)